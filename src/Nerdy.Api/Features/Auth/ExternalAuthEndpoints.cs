using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Auth;

/// <summary>
/// Microsoft / Google sign-in for the desktop app (RFC 8252): the app opens the system browser on <c>start</c> with
/// its loopback redirect and an S256 code challenge; after the provider round-trip, <c>callback</c> redirects to the
/// loopback with a single-use code, which the app redeems at <c>exchange</c> together with its code verifier.
/// </summary>
public static class ExternalAuthEndpoints
{
    private const string ProviderItem = "nerdy.provider";
    private const string RedirectUriItem = "nerdy.redirect_uri";
    private const string StateItem = "nerdy.state";
    private const string CodeChallengeItem = "nerdy.code_challenge";

    public static RouteGroupBuilder MapExternalAuthEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapGet("/external/{provider}/start", StartAsync);
        auth.MapGet("/external/callback", CallbackAsync).ExcludeFromDescription();
        auth.MapPost("/external/exchange", ExchangeAsync);
        return auth;
    }

    /// <summary>Sends provider-side failures (user cancelled, provider error) back to the app instead of a server error.</summary>
    public static void HandleProviderErrors(OAuthEvents events)
    {
        events.OnAccessDenied = async context =>
        {
            await RespondWithErrorAsync(context.HttpContext, context.Properties, ExternalSignInErrors.AccessDenied);
            context.HandleResponse();
        };
        events.OnRemoteFailure = async context =>
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ExternalAuthEndpoints))
                .LogWarning(context.Failure, "External sign-in failed at the provider");
            await RespondWithErrorAsync(context.HttpContext, context.Properties, ExternalSignInErrors.ProviderError);
            context.HandleResponse();
        };
    }

    private static async Task<IResult> StartAsync(
        string provider,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "state")] string? state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        HttpContext http,
        IAuthenticationSchemeProvider schemes)
    {
        var scheme = (await schemes.GetConfiguredProvidersAsync())
            .FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
        if (scheme is null)
        {
            return ApiProblems.Problem(StatusCodes.Status400BadRequest, ApiErrorCodes.ProviderNotConfigured,
                $"Sign-in with '{provider}' is not available on this server.");
        }

        if (!ExternalSignInRules.IsLoopbackRedirect(redirectUri))
        {
            return ApiProblems.Problem(StatusCodes.Status400BadRequest, ApiErrorCodes.InvalidRedirectUri,
                "redirect_uri must be http://127.0.0.1:{port}/... or http://localhost:{port}/...");
        }

        var errors = new ValidationErrors();
        if (!ExternalSignInRules.IsCodeChallenge(codeChallenge))
        {
            errors.Add("code_challenge", "Must be base64url(SHA-256(code_verifier)) without padding (43 characters).");
        }

        errors.MaxLength("state", state, ExternalSignInRules.MaxStateLength);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var properties = new AuthenticationProperties { RedirectUri = $"{http.Request.PathBase}/api/auth/external/callback" };
        properties.Items[ProviderItem] = scheme;
        properties.Items[RedirectUriItem] = redirectUri;
        properties.Items[StateItem] = state;
        properties.Items[CodeChallengeItem] = codeChallenge;
        return TypedResults.Challenge(properties, [scheme]);
    }

    private static async Task<IResult> CallbackAsync(HttpContext http, ExternalLoginService externalLogins, CancellationToken cancellationToken)
    {
        var result = await http.AuthenticateAsync(AuthSetup.ExternalScheme);
        await http.SignOutAsync(AuthSetup.ExternalScheme);

        var items = result.Properties?.Items;
        if (!result.Succeeded
            || items is null
            || !items.TryGetValue(RedirectUriItem, out var redirectUri)
            || !ExternalSignInRules.IsLoopbackRedirect(redirectUri)
            || !items.TryGetValue(ProviderItem, out var provider) || provider is null
            || !items.TryGetValue(CodeChallengeItem, out var codeChallenge) || codeChallenge is null)
        {
            return AuthPages.Message(StatusCodes.Status400BadRequest, "Sign-in didn't complete",
                "This sign-in has expired or was already used. Please start again from Nerdy.");
        }

        items.TryGetValue(StateItem, out var state);
        var identity = ExternalLoginService.ReadIdentity(provider, result.Principal!);
        var (user, error) = identity is null
            ? (null, ExternalSignInErrors.ProviderError)
            : await externalLogins.ResolveUserAsync(identity, cancellationToken);

        if (user is null)
        {
            return Results.Redirect(ExternalSignInRules.BuildRedirect(redirectUri!, ("error", error), ("state", state)));
        }

        var code = await externalLogins.CreateCodeAsync(user.Id, codeChallenge, redirectUri!, cancellationToken);
        return Results.Redirect(ExternalSignInRules.BuildRedirect(redirectUri!, ("code", code), ("state", state)));
    }

    private static async Task<Results<Ok<AuthResponse>, ProblemHttpResult>> ExchangeAsync(
        ExternalExchangeRequest request, ExternalLoginService externalLogins, CancellationToken cancellationToken)
    {
        var response = await externalLogins.ExchangeAsync(request, cancellationToken);
        return response is null
            ? ApiProblems.Problem(StatusCodes.Status400BadRequest, ApiErrorCodes.InvalidCode,
                "The sign-in code is invalid or has expired. Please sign in again.")
            : TypedResults.Ok(response);
    }

    private static Task RespondWithErrorAsync(HttpContext http, AuthenticationProperties? properties, string error)
    {
        var items = properties?.Items;
        if (items is not null
            && items.TryGetValue(RedirectUriItem, out var redirectUri)
            && ExternalSignInRules.IsLoopbackRedirect(redirectUri))
        {
            items.TryGetValue(StateItem, out var state);
            http.Response.Redirect(ExternalSignInRules.BuildRedirect(redirectUri!, ("error", error), ("state", state)));
            return Task.CompletedTask;
        }

        // Without a trustworthy redirect there is no way back to the app; tell the person in the browser.
        return AuthPages.Message(StatusCodes.Status400BadRequest, "Sign-in didn't complete",
            "The sign-in provider reported a problem. Please start again from Nerdy.").ExecuteAsync(http);
    }
}
