using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Nerdy.Api.Data;

namespace Nerdy.Api.Features.Auth;

/// <summary>Input rules for the desktop external sign-in flow (RFC 8252 loopback redirect + RFC 7636 S256 challenge).</summary>
public static partial class ExternalSignInRules
{
    public const int MaxStateLength = 1_024;

    /// <summary>
    /// Only <c>http://127.0.0.1:{port}/…</c> or <c>http://localhost:{port}/…</c> with an explicit port: the desktop app
    /// listens there, and nothing else may receive a sign-in code.
    /// </summary>
    public static bool IsLoopbackRedirect(string? value) =>
        value is { Length: > 0 and <= FieldLimits.RedirectUri }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Host is "127.0.0.1" or "localhost"
        && !uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0;

    /// <summary>base64url(SHA-256(verifier)) without padding is always 43 characters.</summary>
    public static bool IsCodeChallenge(string? value) => value is not null && ChallengePattern().IsMatch(value);

    public static bool IsCodeVerifier(string? value) => value is not null && VerifierPattern().IsMatch(value);

    /// <summary>Appends <paramref name="parameters"/> (skipping nulls) to a validated loopback redirect URI.</summary>
    public static string BuildRedirect(string redirectUri, params (string Name, string? Value)[] parameters) =>
        QueryHelpers.AddQueryString(redirectUri, parameters
            .Where(p => p.Value is not null)
            .Select(p => new KeyValuePair<string, string?>(p.Name, p.Value)));

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex ChallengePattern();

    [GeneratedRegex(@"^[A-Za-z0-9\-._~]{43,128}$")]
    private static partial Regex VerifierPattern();
}
