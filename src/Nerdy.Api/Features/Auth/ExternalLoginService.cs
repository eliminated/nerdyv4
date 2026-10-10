using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Auth;

/// <summary>What an external provider told us about the person signing in.</summary>
/// <param name="Email">Null unless the provider vouches for the address (see <see cref="ExternalLoginService.ReadIdentity"/>).</param>
public sealed record ExternalIdentity(string Provider, string ProviderKey, string? Email, bool EmailVerified, string? Name);

/// <summary>Claims we add to the provider handlers' defaults (see <see cref="AuthSetup"/>).</summary>
public static class ExternalClaims
{
    public const string EmailVerified = "nerdy:email_verified";
    public const string UserPrincipalName = "nerdy:upn";
}

/// <summary>Error values sent to the app's loopback redirect as <c>?error=…&amp;state=…</c>.</summary>
public static class ExternalSignInErrors
{
    public const string AccessDenied = "access_denied";
    public const string ProviderError = "provider_error";
    public const string EmailUnavailable = "email_unavailable";
    public const string EmailNotVerified = "email_not_verified";
    public const string ServerError = "server_error";
}

public sealed class ExternalLoginService(
    NerdyDbContext db,
    UserManager<AppUser> users,
    TokenService tokens,
    TimeProvider time,
    ILogger<ExternalLoginService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);
    private const int CodeBytes = 32;

    /// <summary>
    /// Reads the provider's claims. Google: the email counts only when <c>email_verified</c> is true.
    /// Microsoft: we use the user principal name rather than Graph's <c>mail</c>, because <c>mail</c> is freely
    /// editable by any Entra tenant admin (the "nOAuth" account-takeover pattern) whereas a UPN must be a domain
    /// the tenant has verified, and for personal accounts it is the Microsoft account's own address.
    /// </summary>
    public static ExternalIdentity? ReadIdentity(string provider, ClaimsPrincipal principal)
    {
        var key = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var name = principal.FindFirstValue(ClaimTypes.Name);
        string? email;
        bool verified;
        if (provider == AuthProviders.Google)
        {
            email = principal.FindFirstValue(ClaimTypes.Email);
            verified = bool.TryParse(principal.FindFirstValue(ExternalClaims.EmailVerified), out var v) && v;
        }
        else
        {
            var upn = principal.FindFirstValue(ExternalClaims.UserPrincipalName);
            email = upn is not null && !upn.Contains("#EXT#", StringComparison.OrdinalIgnoreCase) ? upn : null;
            verified = email is not null;
        }

        email = email is not null && new EmailAddressAttribute().IsValid(email) ? email : null;
        return new ExternalIdentity(provider, key, email, verified, name);
    }

    /// <summary>
    /// Finds the account for an external login: by the login itself, else by verified email (linking the login),
    /// else creates a new account. Returns an <see cref="ExternalSignInErrors"/> value on failure.
    /// </summary>
    public async Task<(AppUser? User, string? Error)> ResolveUserAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        var user = await users.FindByLoginAsync(identity.Provider, identity.ProviderKey);
        if (user is not null)
        {
            return (user, null);
        }

        if (identity.Email is null)
        {
            return (null, ExternalSignInErrors.EmailUnavailable);
        }

        if (!identity.EmailVerified)
        {
            return (null, ExternalSignInErrors.EmailNotVerified);
        }

        user = await users.FindByEmailAsync(identity.Email);
        IdentityResult result;
        if (user is null)
        {
            user = new AppUser
            {
                Email = identity.Email,
                UserName = identity.Email,
                EmailConfirmed = true,
                DisplayName = DisplayNameFor(identity),
                CreatedAt = time.GetUtcNow(),
            };
            result = await users.CreateAsync(user);
        }
        else if (!user.EmailConfirmed)
        {
            // Someone registered this address with a password but never proved they own it; the provider just did.
            // Drop the unproven password and its sessions so a squatter cannot keep access to the owner's account.
            if (user.PasswordHash is not null)
            {
                await users.RemovePasswordAsync(user);
            }

            user.EmailConfirmed = true;
            result = await users.UpdateAsync(user);
            await tokens.RevokeAllAsync(user.Id, cancellationToken);
        }
        else
        {
            result = IdentityResult.Success;
        }

        if (result.Succeeded)
        {
            result = await users.AddLoginAsync(user, new UserLoginInfo(identity.Provider, identity.ProviderKey, identity.Provider));
        }

        if (!result.Succeeded)
        {
            logger.LogError("External sign-in with {Provider} failed: {Errors}",
                identity.Provider, string.Join(", ", result.Errors.Select(e => e.Code)));
            return (null, ExternalSignInErrors.ServerError);
        }

        logger.LogInformation("Linked {Provider} login to user {UserId}", identity.Provider, user.Id);
        return (user, null);
    }

    /// <summary>Creates the single-use code for the loopback redirect; only its hash is stored.</summary>
    public async Task<string> CreateCodeAsync(Guid userId, string codeChallenge, string redirectUri, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.ExternalLoginCodes.Where(c => c.UserId == userId && c.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        var code = Secrets.NewToken(CodeBytes);
        db.ExternalLoginCodes.Add(new ExternalLoginCode
        {
            CodeHash = Secrets.Hash(code),
            UserId = userId,
            CodeChallenge = codeChallenge,
            RedirectUri = redirectUri,
            ExpiresAt = now + CodeLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        return code;
    }

    /// <summary>
    /// Redeems a code: it must be unused and unexpired, the redirect URI must match exactly, and
    /// base64url(SHA-256(verifier)) must equal the challenge. A wrong verifier does not burn the code.
    /// </summary>
    public async Task<AuthResponse?> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Code) || !ExternalSignInRules.IsCodeVerifier(request.CodeVerifier))
        {
            return null;
        }

        var hash = Secrets.Hash(request.Code);
        var code = await db.ExternalLoginCodes.AsNoTracking().SingleOrDefaultAsync(c => c.CodeHash == hash, cancellationToken);
        var now = time.GetUtcNow();
        if (code is null
            || code.UsedAt is not null
            || code.ExpiresAt <= now
            || !string.Equals(code.RedirectUri, request.RedirectUri, StringComparison.Ordinal)
            || !Secrets.FixedTimeEquals(Secrets.Hash(request.CodeVerifier), code.CodeChallenge))
        {
            return null;
        }

        var redeemed = await db.ExternalLoginCodes
            .Where(c => c.CodeHash == hash && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), cancellationToken);
        if (redeemed != 1)
        {
            return null;
        }

        var user = await users.FindByIdAsync(code.UserId.ToString());
        return user is null ? null : await tokens.IssueAsync(user, cancellationToken);
    }

    private static string DisplayNameFor(ExternalIdentity identity)
    {
        var name = string.IsNullOrWhiteSpace(identity.Name) ? identity.Email![..identity.Email!.IndexOf('@')] : identity.Name.Trim();
        return name.Length > FieldLimits.DisplayName ? name[..FieldLimits.DisplayName] : name;
    }
}
