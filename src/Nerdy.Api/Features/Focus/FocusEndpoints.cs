using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Focus;

public static class FocusEndpoints
{
    public const string ExitReviewPolicy = "exit-review";
    private const int MaxExplanation = 1000;

    public static IServiceCollection AddNerdyFocus(this IServiceCollection services)
    {
        services.AddOptions<ExitReviewOptions>().BindConfiguration(ExitReviewOptions.Section);
        services.AddSingleton<IExitReviewer, ClaudeExitReviewer>();
        // Per-account budget for AI reviews (cost control). Hitting it allows the exit (the app fails open on 429).
        services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
            options.AddPolicy(ExitReviewPolicy, httpContext =>
            {
                var limit = httpContext.RequestServices.GetRequiredService<IOptions<ExitReviewOptions>>().Value.PerUserPerHour;
                var user = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.User.FindFirstValue("sub") ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0
                });
            }));
        return services;
    }

    public static RouteGroupBuilder MapFocusEndpoints(this RouteGroupBuilder api)
    {
        var focus = api.MapGroup("/focus").WithTags("Focus").RequireAuthorization();
        focus.MapPost("/exit-review", ReviewExitAsync).RequireRateLimiting(ExitReviewPolicy);
        return api;
    }

    private static async Task<Results<Ok<ExitReviewResponse>, ValidationProblem>> ReviewExitAsync(
        ExitReviewRequest request, IExitReviewer reviewer, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        errors.Defined("reason", request.Reason);
        errors.Required("explanation", request.Explanation, MaxExplanation);
        if (errors.Any) return errors.ToProblem();

        return TypedResults.Ok(await reviewer.ReviewAsync(request, cancellationToken));
    }
}
