using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nerdy.Api.Features.Focus;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

// No test here reaches the Claude API: the factory has no API key, and the reviewable-path test uses a fake.
public sealed class FocusExitReviewTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    private static ExitReviewRequest Request(ExitReason reason, string explanation = "Group project files broke an hour before the deadline") => new()
    {
        Reason = reason,
        Explanation = explanation,
        Task = "Methods draft",
        MinutesLeft = 18,
        PlannedMinutes = 50
    };

    [Fact]
    public async Task Requires_sign_in()
    {
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/focus/exit-review", Request(ExitReason.Other));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Explanation_is_required()
    {
        var user = await factory.RegisterAsync();

        var response = await user.Client.PostAsJsonAsync("/api/focus/exit-review", Request(ExitReason.Other, "  "));

        Assert.Contains("explanation", await response.ReadValidationErrorsAsync());
    }

    [Fact]
    public async Task Without_an_api_key_every_exit_is_allowed_unreviewed()
    {
        var user = await factory.RegisterAsync();

        var result = await (await user.Client.PostAsJsonAsync("/api/focus/exit-review", Request(ExitReason.Other))).ReadAsync<ExitReviewResponse>();

        Assert.True(result.Allowed);
        Assert.False(result.Reviewed);
    }

    [Theory]
    [InlineData(ExitReason.Medical)]
    [InlineData(ExitReason.Family)]
    public async Task Emergencies_are_never_reviewed_even_when_configured(ExitReason reason)
    {
        // A key is configured, yet nothing is sent: the reviewer returns before any network call.
        var reviewer = new ClaudeExitReviewer(Options.Create(new ExitReviewOptions { ApiKey = "sk-test-not-used" }), NullLogger<ClaudeExitReviewer>.Instance);

        var result = await reviewer.ReviewAsync(Request(reason, "short"), CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.False(result.Reviewed);
        Assert.False(ClaudeExitReviewer.IsReviewable(reason));
    }

    [Fact]
    public async Task A_stay_decision_is_returned_to_the_app()
    {
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IExitReviewer>(new FixedReviewer(new ExitReviewResponse { Allowed = false, Message = "Only 18 minutes to go.", Reviewed = true }))));
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var auth = await (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = TestApi.NewEmail(), Password = TestApi.Password, DisplayName = "Stay" })).ReadAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);

        var result = await (await client.PostAsJsonAsync("/api/focus/exit-review", Request(ExitReason.Urgent))).ReadAsync<ExitReviewResponse>();

        Assert.False(result.Allowed);
        Assert.Equal("Only 18 minutes to go.", result.Message);
    }

    private sealed class FixedReviewer(ExitReviewResponse response) : IExitReviewer
    {
        public Task<ExitReviewResponse> ReviewAsync(ExitReviewRequest request, CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
