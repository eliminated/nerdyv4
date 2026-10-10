using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Nerdy.Contracts;

namespace Nerdy.Api.Infrastructure;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Requests per client IP per minute to the anonymous auth endpoints.</summary>
    public int AuthPerMinute { get; set; } = 20;
}

public static class RateLimiting
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddNerdyRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.Section);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests. Please wait a minute and try again.",
                        Extensions = { ["code"] = ApiErrorCodes.RateLimited },
                    },
                });
            };

            // Fixed window per client IP. Behind App Service the IP comes from X-Forwarded-For (see Program.cs).
            options.AddPolicy(AuthPolicy, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.AuthPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });
        return services;
    }
}
