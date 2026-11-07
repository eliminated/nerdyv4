using Microsoft.AspNetCore.Http.HttpResults;
using Nerdy.Contracts;

namespace Nerdy.Api.Infrastructure;

/// <summary>RFC 7807 problem results carrying a machine-readable <c>code</c> (see <see cref="ApiErrorCodes"/>).</summary>
public static class ApiProblems
{
    public static ProblemHttpResult Problem(int status, string code, string title, string? detail = null) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult NotFound(string what) =>
        Problem(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, $"{what} not found.");

    public static ProblemHttpResult Forbidden(string code, string title) =>
        Problem(StatusCodes.Status403Forbidden, code, title);

    public static ProblemHttpResult Conflict(string code, string title) =>
        Problem(StatusCodes.Status409Conflict, code, title);

    /// <summary>426 Upgrade Required: the app is too old for this request and must update first.</summary>
    public static ProblemHttpResult UpdateRequired(string title) =>
        Problem(StatusCodes.Status426UpgradeRequired, ApiErrorCodes.UpdateRequired, title);
}
