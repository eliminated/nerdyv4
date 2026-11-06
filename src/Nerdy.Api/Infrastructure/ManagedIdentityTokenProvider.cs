using System.Text.Json;
using Anthropic.Credentials;

namespace Nerdy.Api.Infrastructure;

/// <summary>
/// Gets a Microsoft Entra token for the web app's managed identity, for Claude API workload identity federation.
/// App Service exposes a local token endpoint (IDENTITY_ENDPOINT / IDENTITY_HEADER); VMs use IMDS instead.
/// Entra issues each token for 24 hours with <c>iat</c> backdated by 5 minutes (exp − iat = 86700 s), so the Anthropic
/// issuer's maximum JWT lifetime must be above that (90000 s).
/// </summary>
public sealed class ManagedIdentityTokenProvider(string audience) : IIdentityTokenProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Non-secret claims of the last token (audience, issuer, version, identity, lifetime), to compare with the
    /// Anthropic federation rule when an exchange is rejected. Never the token itself.
    /// </summary>
    public string? LastTokenClaims { get; private set; }

    public async Task<string> GetIdentityTokenAsync(CancellationToken cancellationToken = default)
    {
        var resource = Uri.EscapeDataString(audience);
        var endpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        using var request = endpoint is not null
            ? new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?api-version=2019-08-01&resource={resource}")
            {
                Headers = { { "X-IDENTITY-HEADER", Environment.GetEnvironmentVariable("IDENTITY_HEADER") } }
            }
            : new HttpRequestMessage(HttpMethod.Get,
                $"http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource={resource}")
            {
                Headers = { { "Metadata", "true" } }
            };

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var token = json.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("The managed identity endpoint returned no access token.");
        LastTokenClaims = DescribeClaims(token);
        return token;
    }

    private static string? DescribeClaims(string jwt)
    {
        try
        {
            var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            var claims = doc.RootElement;
            string Claim(string name) => claims.TryGetProperty(name, out var value) ? value.ToString() : "-";
            var lifetime = claims.TryGetProperty("exp", out var exp) && claims.TryGetProperty("iat", out var iat)
                ? exp.GetInt64() - iat.GetInt64()
                : 0;
            return $"aud={Claim("aud")} iss={Claim("iss")} ver={Claim("ver")} oid={Claim("oid")} tid={Claim("tid")} " +
                   $"lifetime={lifetime}s jti={(claims.TryGetProperty("jti", out _) ? "present" : "absent")}";
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }
}
