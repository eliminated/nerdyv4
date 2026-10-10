using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests.Infrastructure;

/// <summary>A signed-in test user: the HTTP client carries their access token.</summary>
public sealed record TestUser(HttpClient Client, AuthResponse Auth, string Email, string Password)
{
    public Guid Id => Auth.User.Id;
}

public static class TestApi
{
    public const string Password = "Passw0rd!";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static async Task<TestUser> RegisterAsync(this NerdyApiFactory factory, string? displayName = null, string? email = null)
    {
        email ??= NewEmail();
        var client = factory.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = email, Password = Password, DisplayName = displayName ?? "Test User" });
        await response.EnsureStatusAsync(HttpStatusCode.OK);

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new TestUser(client, auth, email, Password);
    }

    public static async Task<TestUser> RegisterTeacherAsync(this NerdyApiFactory factory, string displayName = "Ms Teacher")
    {
        var user = await factory.RegisterAsync(displayName);
        var response = await user.Client.PatchAsJsonAsync("/api/me", new UpdateProfileRequest { Role = UserRole.Teacher });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return user;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await response.EnsureStatusAsync(expected);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<T> GetAsync<T>(this HttpClient client, string url) => await (await client.GetAsync(url)).ReadAsync<T>();

    public static async Task<SyncResponse> SyncAsync(this HttpClient client, long sinceVersion, SyncChanges? changes = null) =>
        await (await client.PostAsJsonAsync("/api/sync", new SyncRequest { SinceVersion = sinceVersion, Changes = changes ?? new() }))
            .ReadAsync<SyncResponse>();

    /// <summary>Asserts the status and that the body is a problem document, returning its <c>code</c> extension (or null).</summary>
    public static async Task<string?> ReadProblemCodeAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        await response.EnsureStatusAsync(expected);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The field names of a 400 validation problem's <c>errors</c> map.</summary>
    public static async Task<IReadOnlyList<string>> ReadValidationErrorsAsync(this HttpResponseMessage response)
    {
        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.RootElement.GetProperty("code").GetString());
        return problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }

    public static async Task EnsureStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}: " +
                        await response.Content.ReadAsStringAsync());
        }
    }
}
