using System.Net;
using System.Net.Http.Json;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

public sealed class HealthTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    [Fact]
    public async Task Health_reports_healthy_with_a_reachable_database()
    {
        var response = await factory.CreateApiClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_routes_are_404_problems()
    {
        var response = await factory.CreateApiClient().GetAsync("/api/nope");
        await response.ReadProblemCodeAsync(HttpStatusCode.NotFound);
    }
}

public sealed class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Anonymous_auth_endpoints_are_rate_limited_per_client()
    {
        var client = factory.CreateApiClient();
        var login = new LoginRequest { Email = TestApi.NewEmail(), Password = "whatever1" };
        for (var i = 0; i < RateLimitedApiFactory.Limit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", login)).StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(ApiErrorCodes.RateLimited, await limited.ReadProblemCodeAsync(HttpStatusCode.TooManyRequests));
        Assert.True(limited.Headers.RetryAfter is not null);

        // Health and other non-auth endpoints are not throttled.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}
