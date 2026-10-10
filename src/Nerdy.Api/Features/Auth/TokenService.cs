using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Features.Me;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Auth;

/// <summary>Issues JWT access tokens and manages rotating refresh tokens (stored as SHA-256 hashes only).</summary>
public sealed class TokenService(
    NerdyDbContext db,
    UserManager<AppUser> users,
    IOptions<JwtOptions> options,
    TimeProvider time,
    ILogger<TokenService> logger)
{
    public const string RoleClaim = "role";
    private const int RefreshTokenBytes = 64;

    private static readonly JsonWebTokenHandler TokenHandler = new();
    private JwtOptions Jwt => options.Value;

    /// <summary>Signs the user in on a new device: a fresh refresh-token family.</summary>
    public Task<AuthResponse> IssueAsync(AppUser user, CancellationToken cancellationToken) =>
        IssueAsync(user, Guid.CreateVersion7(), Secrets.NewToken(RefreshTokenBytes), cancellationToken);

    /// <summary>
    /// Rotates a refresh token. Returns null (→ 401) when it is unknown, expired, or was already used — in the last
    /// case the whole family is revoked, because either the client lost a race or someone else holds a copy.
    /// </summary>
    public async Task<AuthResponse?> RefreshAsync(string presentedToken, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(presentedToken);
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var now = time.GetUtcNow();
        if (token is null || token.ExpiresAt <= now)
        {
            return null;
        }

        // Claim the token atomically (only one of two concurrent refreshes can) and link it to its successor.
        var successor = Secrets.NewToken(RefreshTokenBytes);
        var successorHash = Secrets.Hash(successor);
        var claimed = token.RevokedAt is null && await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByHash, successorHash), cancellationToken) == 1;

        if (!claimed)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking token family {FamilyId}", token.UserId, token.FamilyId);
            await RevokeFamilyAsync(token.FamilyId, cancellationToken);
            return null;
        }

        var user = await users.FindByIdAsync(token.UserId.ToString());
        return user is null ? null : await IssueAsync(user, token.FamilyId, successor, cancellationToken);
    }

    /// <summary>Signs a device out: revokes the presented token's family. Unknown tokens are ignored.</summary>
    public async Task RevokeAsync(string presentedToken, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(presentedToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);
        if (familyId is { } family)
        {
            await RevokeFamilyAsync(family, cancellationToken);
        }
    }

    /// <summary>Signs the user out everywhere (password reset, takeover of an unconfirmed account).</summary>
    public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    private Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    private async Task<AuthResponse> IssueAsync(AppUser user, Guid familyId, string refreshToken, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var accessExpires = now.AddMinutes(Jwt.AccessTokenMinutes);
        var refreshExpires = now.AddDays(Jwt.RefreshTokenDays);

        // Expired tokens are no longer useful for reuse detection; prune this user's on every issue
        // instead of running a background job that would keep a serverless database awake.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Secrets.Hash(refreshToken),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = refreshExpires,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = CreateAccessToken(user, now, accessExpires),
            AccessTokenExpiresAt = accessExpires,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = refreshExpires,
            User = await users.ToDtoAsync(user),
        };
    }

    private string CreateAccessToken(AppUser user, DateTimeOffset now, DateTimeOffset expires) =>
        TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Jwt.Issuer,
            Audience = Jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(Jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email ?? "",
                [JwtRegisteredClaimNames.Name] = user.DisplayName,
                [RoleClaim] = user.Role.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        });
}
