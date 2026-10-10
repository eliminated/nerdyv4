using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Features.Auth;
using Nerdy.Api.Infrastructure;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

public sealed class ExternalAuthTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    private const string RedirectUri = "http://127.0.0.1:50123/callback";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk-dBjftJeZ4CVP";
    private static readonly string Challenge = Secrets.Hash(Verifier);

    [Theory]
    [InlineData("https://evil.com/callback")]
    [InlineData("http://evil.com:50123/callback")]
    [InlineData("https://127.0.0.1:50123/callback")]
    [InlineData("http://127.0.0.1/callback")]
    [InlineData("http://127.0.0.1.evil.com:50123/callback")]
    [InlineData("http://user@127.0.0.1:50123/callback")]
    [InlineData("nerdy://callback")]
    [InlineData("/relative")]
    public async Task Start_rejects_non_loopback_redirect_uris(string redirectUri)
    {
        var response = await factory.CreateApiClient().GetAsync(StartUrl("google", redirectUri, Challenge));
        Assert.Equal(ApiErrorCodes.InvalidRedirectUri, await response.ReadProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Theory]
    [InlineData("http://127.0.0.1:50123/callback")]
    [InlineData("http://localhost:7777/")]
    public async Task Start_accepts_loopback_redirect_uris_and_challenges_the_provider(string redirectUri)
    {
        var response = await factory.CreateApiClient().GetAsync(StartUrl("google", redirectUri, Challenge));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://accounts.google.com/", response.Headers.Location!.ToString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_rejects_a_malformed_code_challenge()
    {
        var response = await factory.CreateApiClient().GetAsync(StartUrl("google", RedirectUri, "too-short"));
        Assert.Contains("code_challenge", await response.ReadValidationErrorsAsync());
    }

    [Fact]
    public async Task Start_rejects_a_provider_that_is_not_configured()
    {
        var response = await factory.CreateApiClient().GetAsync(StartUrl("microsoft", RedirectUri, Challenge));
        Assert.Equal(ApiErrorCodes.ProviderNotConfigured, await response.ReadProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Exchange_returns_tokens_once_and_the_code_is_single_use()
    {
        var user = await factory.RegisterAsync();
        var code = await SeedCodeAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(2));
        var client = factory.CreateApiClient();

        var auth = await (await client.PostAsJsonAsync("/api/auth/external/exchange", Exchange(code))).ReadAsync<AuthResponse>();
        Assert.Equal(user.Id, auth.User.Id);
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));

        var again = await client.PostAsJsonAsync("/api/auth/external/exchange", Exchange(code));
        Assert.Equal(ApiErrorCodes.InvalidCode, await again.ReadProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Exchange_with_the_wrong_verifier_fails_without_burning_the_code()
    {
        var user = await factory.RegisterAsync();
        var code = await SeedCodeAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(2));
        var client = factory.CreateApiClient();

        var wrong = await client.PostAsJsonAsync("/api/auth/external/exchange",
            Exchange(code) with { CodeVerifier = new string('x', 43) });
        Assert.Equal(ApiErrorCodes.InvalidCode, await wrong.ReadProblemCodeAsync(HttpStatusCode.BadRequest));

        var right = await client.PostAsJsonAsync("/api/auth/external/exchange", Exchange(code));
        await right.EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Exchange_with_a_different_redirect_uri_fails()
    {
        var user = await factory.RegisterAsync();
        var code = await SeedCodeAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(2));

        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/external/exchange",
            Exchange(code) with { RedirectUri = "http://127.0.0.1:50124/callback" });
        Assert.Equal(ApiErrorCodes.InvalidCode, await response.ReadProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Exchange_of_an_expired_code_fails()
    {
        var user = await factory.RegisterAsync();
        var code = await SeedCodeAsync(user.Id, DateTimeOffset.UtcNow.AddSeconds(-1));

        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/external/exchange", Exchange(code));
        Assert.Equal(ApiErrorCodes.InvalidCode, await response.ReadProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Created_codes_expire_after_two_minutes_and_store_only_a_hash()
    {
        var user = await factory.RegisterAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ExternalLoginService>();

        var code = await service.CreateCodeAsync(user.Id, Challenge, RedirectUri, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<NerdyDbContext>();
        var stored = await db.ExternalLoginCodes.AsNoTracking().SingleAsync(c => c.UserId == user.Id);
        Assert.Equal(Secrets.Hash(code), stored.CodeHash);
        Assert.InRange(stored.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1.9), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task A_new_external_identity_creates_a_confirmed_account()
    {
        var email = TestApi.NewEmail();
        var (user, error) = await ResolveAsync(new ExternalIdentity(AuthProviders.Google, Guid.NewGuid().ToString(), email, true, "Grace Hopper"));

        Assert.Null(error);
        Assert.Equal(email, user!.Email);
        Assert.Equal("Grace Hopper", user.DisplayName);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task An_external_identity_links_to_the_existing_account_with_that_email_and_is_found_again_by_login()
    {
        var existing = await factory.RegisterAsync();
        var identity = new ExternalIdentity(AuthProviders.Google, Guid.NewGuid().ToString(), existing.Email, true, "Someone");

        var (linked, _) = await ResolveAsync(identity);
        Assert.Equal(existing.Id, linked!.Id);
        Assert.Contains(AuthProviders.Google, (await existing.Client.GetAsync<UserDto>("/api/me")).ExternalProviders);

        var (again, _) = await ResolveAsync(identity with { Email = null });
        Assert.Equal(existing.Id, again!.Id);
    }

    [Fact]
    public async Task An_unverified_google_email_is_refused()
    {
        var (user, error) = await ResolveAsync(new ExternalIdentity(AuthProviders.Google, Guid.NewGuid().ToString(), TestApi.NewEmail(), false, null));
        Assert.Null(user);
        Assert.Equal(ExternalSignInErrors.EmailNotVerified, error);
    }

    private async Task<(AppUser? User, string? Error)> ResolveAsync(ExternalIdentity identity)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ExternalLoginService>().ResolveUserAsync(identity, CancellationToken.None);
    }

    private async Task<string> SeedCodeAsync(Guid userId, DateTimeOffset expiresAt)
    {
        var code = Secrets.NewToken(32);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NerdyDbContext>();
        db.ExternalLoginCodes.Add(new ExternalLoginCode
        {
            CodeHash = Secrets.Hash(code),
            UserId = userId,
            CodeChallenge = Challenge,
            RedirectUri = RedirectUri,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return code;
    }

    private static ExternalExchangeRequest Exchange(string code) =>
        new() { Code = code, CodeVerifier = Verifier, RedirectUri = RedirectUri };

    private static string StartUrl(string provider, string redirectUri, string challenge) =>
        QueryHelpers.AddQueryString($"/api/auth/external/{provider}/start", new Dictionary<string, string?>
        {
            ["redirect_uri"] = redirectUri,
            ["state"] = "xyz",
            ["code_challenge"] = challenge,
        });
}
