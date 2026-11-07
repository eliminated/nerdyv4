using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Sync;

public static class SyncEndpoints
{
    /// <summary>
    /// A first sync can carry many notes (up to 2 MB of RTF each), so this endpoint accepts more than Kestrel's
    /// 30 MB default. Clients with more than that pending should push in batches.
    /// </summary>
    private const long MaxRequestBytes = 100 * 1024 * 1024;

    public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/sync", SyncAsync)
            .WithTags("Sync")
            .RequireAuthorization()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));
        return api;
    }

    private static async Task<Results<Ok<SyncResponse>, ValidationProblem, ProblemHttpResult>> SyncAsync(
        SyncRequest request, ClaimsPrincipal principal, SyncService sync, CancellationToken cancellationToken)
    {
        // An older app would read subjects as topics and topics as subtopics, so it must update before it syncs.
        // Its unsynced edits wait on the PC, and the updated app converts them.
        if (request.Protocol < SyncProtocol.Current)
        {
            return ApiProblems.UpdateRequired("This version of Nerdy can't sync any more. Restart Nerdy to update it.");
        }

        var errors = SyncValidator.Validate(request);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var response = await sync.SyncAsync(principal.GetUserId(), request, cancellationToken);
        return response is null
            ? ApiProblems.Conflict(ApiErrorCodes.SyncConflict, "Another sync of this account is in progress. Please try again.")
            : TypedResults.Ok(response);
    }
}
