using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

public sealed class AuthTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    [Fact]
    public async Task Register_then_login_then_me_returns_the_profile()
    {
        var email = TestApi.NewEmail();
        var registered = await factory.RegisterAsync("Ada Lovelace", email);

        Assert.Equal(email, registered.Auth.User.Email);
        Assert.Equal(UserRole.Student, registered.Auth.User.Role);
        Assert.False(registered.Auth.User.EmailConfirmed);
        Assert.True(registered.Auth.User.HasPassword);
        Assert.InRange(registered.Auth.AccessTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        Assert.InRange(registered.Auth.RefreshTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30));

        var client = factory.CreateApiClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email.ToUpperInvariant(), Password = TestApi.Password }))
            .ReadAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var me = await client.GetAsync<UserDto>("/api/me");
        Assert.Equal(registered.Id, me.Id);
        Assert.Equal("Ada Lovelace", me.DisplayName);
    }

    [Fact]
    public async Task Me_without_a_token_is_a_401_problem()
    {
        var response = await factory.CreateApiClient().GetAsync("/api/me");
        await response.ReadProblemCodeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_the_wrong_password_is_rejected()
    {
        var user = await factory.RegisterAsync();
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = user.Email, Password = "wrong-password-1" });

        Assert.Equal(ApiErrorCodes.InvalidCredentials, await response.ReadProblemCodeAsync(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var user = await factory.RegisterAsync();
        var client = factory.CreateApiClient();
        for (var i = 0; i < 4; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = "wrong-password-1" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var fifth = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = "wrong-password-1" });
        Assert.Equal(ApiErrorCodes.LockedOut, await fifth.ReadProblemCodeAsync(HttpStatusCode.Forbidden));

        var correct = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = user.Password });
        Assert.Equal(ApiErrorCodes.LockedOut, await correct.ReadProblemCodeAsync(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Registering_a_taken_email_is_a_conflict()
    {
        var user = await factory.RegisterAsync();
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = user.Email.ToUpperInvariant(), Password = TestApi.Password, DisplayName = "Copycat" });

        Assert.Equal(ApiErrorCodes.EmailTaken, await response.ReadProblemCodeAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_weak_password_is_a_validation_problem()
    {
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = TestApi.NewEmail(), Password = "nodigits", DisplayName = "Weak" });

        Assert.Contains("password", await response.ReadValidationErrorsAsync());
    }

    [Fact]
    public async Task A_request_missing_required_members_is_a_400_problem()
    {
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", new { email = "someone@example.com" });
        await response.ReadProblemCodeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reusing_an_old_one_revokes_the_family()
    {
        var user = await factory.RegisterAsync();
        var client = factory.CreateApiClient();

        var first = await (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = user.Auth.RefreshToken }))
            .ReadAsync<AuthResponse>();
        Assert.NotEqual(user.Auth.RefreshToken, first.RefreshToken);

        var second = await (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = first.RefreshToken }))
            .ReadAsync<AuthResponse>();

        // Replaying the original token (already rotated) looks like theft: the whole family dies...
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = user.Auth.RefreshToken });
        Assert.Equal(ApiErrorCodes.InvalidRefreshToken, await replay.ReadProblemCodeAsync(HttpStatusCode.Unauthorized));

        // ...including the newest token, which was still valid a moment ago.
        var newest = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = second.RefreshToken });
        Assert.Equal(ApiErrorCodes.InvalidRefreshToken, await newest.ReadProblemCodeAsync(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task A_new_sign_in_is_not_affected_by_another_family_being_revoked()
    {
        var user = await factory.RegisterAsync();
        var client = factory.CreateApiClient();
        var otherDevice = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = user.Password }))
            .ReadAsync<AuthResponse>();

        await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = user.Auth.RefreshToken });
        await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = user.Auth.RefreshToken }); // reuse

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = otherDevice.RefreshToken });
        await refreshed.EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var user = await factory.RegisterAsync();
        var client = factory.CreateApiClient();

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest { RefreshToken = user.Auth.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = user.Auth.RefreshToken });
        Assert.Equal(ApiErrorCodes.InvalidRefreshToken, await refresh.ReadProblemCodeAsync(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Forgot_password_is_202_for_an_unknown_email_and_sends_nothing()
    {
        var email = TestApi.NewEmail();
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Null(factory.Emails.LastTo(email));
    }

    [Fact]
    public async Task Password_reset_with_the_emailed_code_changes_the_password_and_signs_out_everywhere()
    {
        var user = await factory.RegisterAsync();
        var client = factory.CreateApiClient();
        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = user.Email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);

        var email = factory.Emails.LastTo(user.Email)!;
        var link = Regex.Match(email.PlainText, @"https://nerdy\.test/api/auth/reset-password#email=[^&]+&token=(\S+)");
        Assert.True(link.Success, email.PlainText);
        var token = Uri.UnescapeDataString(link.Groups[1].Value);

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest { Email = user.Email, Token = token, NewPassword = "N3wPassword" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var oldPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        var newPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email, Password = "N3wPassword" });
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);

        var oldSession = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = user.Auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);

        var reused = await client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest { Email = user.Email, Token = token, NewPassword = "An0therPassword" });
        Assert.Equal(ApiErrorCodes.InvalidResetToken, await reused.ReadProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task The_confirmation_link_confirms_the_email()
    {
        var user = await factory.RegisterAsync();
        var email = factory.Emails.LastTo(user.Email)!;
        var link = Regex.Match(email.PlainText, @"https://nerdy\.test(/api/auth/confirm-email\?\S+)").Groups[1].Value;

        var page = await factory.CreateApiClient().GetAsync(link);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Email confirmed", await page.Content.ReadAsStringAsync());

        Assert.True((await user.Client.GetAsync<UserDto>("/api/me")).EmailConfirmed);
    }

    [Fact]
    public async Task A_tampered_confirmation_link_shows_an_error_page()
    {
        var user = await factory.RegisterAsync();
        var page = await factory.CreateApiClient().GetAsync($"/api/auth/confirm-email?userId={user.Id}&token=bm90LWEtdG9rZW4");

        Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
        Assert.False((await user.Client.GetAsync<UserDto>("/api/me")).EmailConfirmed);
    }

    [Fact]
    public async Task Providers_lists_only_configured_providers()
    {
        var providers = await factory.CreateApiClient().GetAsync<string[]>("/api/auth/providers");
        Assert.Equal([AuthProviders.Google], providers);
    }
}
