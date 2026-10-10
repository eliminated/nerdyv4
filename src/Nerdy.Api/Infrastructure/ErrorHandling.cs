using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace Nerdy.Api.Infrastructure;

public static class ErrorHandling
{
    /// <summary>
    /// Every error leaves the API as RFC 7807 problem details: thrown exceptions via the exception handler,
    /// empty 4xx/5xx responses (401 from JWT bearer, unknown routes, ...) via status code pages.
    /// </summary>
    public static IServiceCollection AddNerdyProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<BadRequestExceptionHandler>();

        // Malformed JSON / missing required members surface as BadHttpRequestException so the handler
        // below can report what was wrong instead of an empty 400.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        return services;
    }
}

/// <summary>Turns request binding failures into a 400 problem that says what could not be read.</summary>
internal sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read.",
                Detail = badRequest.InnerException is JsonException json ? json.Message : badRequest.Message,
            },
        });
    }
}
