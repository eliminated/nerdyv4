using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(RateLimiting.AuthPolicy);

        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/refresh", RefreshAsync);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapPost("/forgot-password", ForgotPasswordAsync);
        auth.MapPost("/reset-password", ResetPasswordAsync);
        auth.MapGet("/reset-password", AuthPages.ResetPassword).ExcludeFromDescription();
        auth.MapGet("/confirm-email", ConfirmEmailAsync).ExcludeFromDescription();
        auth.MapPost("/resend-confirmation", ResendConfirmationAsync).RequireAuthorization();
        auth.MapGet("/providers", async (IAuthenticationSchemeProvider schemes) => TypedResults.Ok(await schemes.GetConfiguredProvidersAsync()))
            .DisableRateLimiting();
        auth.MapExternalAuthEndpoints();
        return api;
    }

    private static async Task<Results<Ok<AuthResponse>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        RegisterRequest request,
        UserManager<AppUser> users,
        TokenService tokens,
        AccountEmails emails,
        TimeProvider time,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.Required("email", request.Email, FieldLimits.Email);
        errors.Required("displayName", request.DisplayName, FieldLimits.DisplayName);
        errors.Required("password", request.Password, FieldLimits.Password);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var email = request.Email.Trim();
        var user = new AppUser
        {
            Email = email,
            UserName = email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow(),
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                return ApiProblems.Conflict(ApiErrorCodes.EmailTaken, "An account with this email already exists.");
            }

            return ToValidationErrors(result).ToProblem();
        }

        await emails.SendConfirmationAsync(user, httpRequest, cancellationToken);
        return TypedResults.Ok(await tokens.IssueAsync(user, cancellationToken));
    }

    private static async Task<Results<Ok<AuthResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return InvalidCredentials();
        }

        // Counts failures and locks the account for 5 minutes after 5 in a row.
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password ?? "", lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return ApiProblems.Problem(StatusCodes.Status403Forbidden, ApiErrorCodes.LockedOut,
                "Too many failed sign-in attempts.", "Try again in a few minutes, or reset your password.");
        }

        return result.Succeeded ? TypedResults.Ok(await tokens.IssueAsync(user, cancellationToken)) : InvalidCredentials();
    }

    private static async Task<Results<Ok<AuthResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request, TokenService tokens, CancellationToken cancellationToken)
    {
        var response = string.IsNullOrEmpty(request.RefreshToken) ? null : await tokens.RefreshAsync(request.RefreshToken, cancellationToken);
        return response is null
            ? ApiProblems.Problem(StatusCodes.Status401Unauthorized, ApiErrorCodes.InvalidRefreshToken,
                "Your session has expired. Please sign in again.")
            : TypedResults.Ok(response);
    }

    private static async Task<NoContent> LogoutAsync(RefreshRequest request, TokenService tokens, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            await tokens.RevokeAsync(request.RefreshToken, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Always 202, whether or not the address has an account.</summary>
    private static async Task<Accepted> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        UserManager<AppUser> users,
        AccountEmails emails,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null)
        {
            await emails.SendPasswordResetAsync(user, httpRequest, cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ResetPasswordAsync(
        ResetPasswordRequest request,
        UserManager<AppUser> users,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.Required("newPassword", request.NewPassword, FieldLimits.Password);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var token = AccountEmails.DecodeToken(request.Token);
        var user = token is null || string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return InvalidResetToken();
        }

        var result = await users.ResetPasswordAsync(user, token!, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return InvalidResetToken();
            }

            return ToValidationErrors(result, passwordField: "newPassword").ToProblem();
        }

        // The reset link proved control of the mailbox: confirm it, lift any lockout, and sign out other devices.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await tokens.RevokeAllAsync(user.Id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ConfirmEmailAsync(string? userId, string? token, UserManager<AppUser> users)
    {
        var decoded = AccountEmails.DecodeToken(token);
        var user = decoded is null || userId is null ? null : await users.FindByIdAsync(userId);
        if (user is not null && (user.EmailConfirmed || (await users.ConfirmEmailAsync(user, decoded!)).Succeeded))
        {
            return AuthPages.Message(StatusCodes.Status200OK, "Email confirmed",
                "Thanks! Your email address is confirmed. You can close this tab and go back to Nerdy.");
        }

        return AuthPages.Message(StatusCodes.Status400BadRequest, "Link expired",
            "This confirmation link is invalid or has expired. Open Nerdy and send a new one from your profile.");
    }

    private static async Task<Accepted> ResendConfirmationAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AccountEmails emails,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is { EmailConfirmed: false })
        {
            await emails.SendConfirmationAsync(user, httpRequest, cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static ProblemHttpResult InvalidCredentials() =>
        ApiProblems.Problem(StatusCodes.Status401Unauthorized, ApiErrorCodes.InvalidCredentials, "Wrong email or password.");

    private static ProblemHttpResult InvalidResetToken() =>
        ApiProblems.Problem(StatusCodes.Status400BadRequest, ApiErrorCodes.InvalidResetToken,
            "This reset code is invalid or has expired. Please request a new one.");

    /// <summary>Maps Identity's validation errors (password rules, email format) to request fields.</summary>
    private static ValidationErrors ToValidationErrors(IdentityResult result, string passwordField = "password")
    {
        var errors = new ValidationErrors();
        foreach (var error in result.Errors)
        {
            var field = error.Code.StartsWith("Password", StringComparison.Ordinal) ? passwordField
                : error.Code.Contains("Email", StringComparison.Ordinal) || error.Code.Contains("UserName", StringComparison.Ordinal) ? "email"
                : "";
            errors.Add(field, error.Description);
        }

        return errors;
    }
}
