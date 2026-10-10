using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Me;

public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me").RequireAuthorization();
        me.MapGet("", GetAsync);
        me.MapPatch("", UpdateAsync);
        me.MapDelete("", DeleteAsync);
        return api;
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> GetAsync(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        return user is null ? ApiProblems.NotFound("Account") : TypedResults.Ok(await users.ToDtoAsync(user));
    }

    private static async Task<Results<Ok<UserDto>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        UpdateProfileRequest request, ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var errors = new ValidationErrors();
        if (request.DisplayName is not null)
        {
            errors.Required("displayName", request.DisplayName, FieldLimits.DisplayName);
        }

        if (request.Role is { } role)
        {
            errors.Defined("role", role);
        }

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null)
        {
            return ApiProblems.NotFound("Account");
        }

        user.DisplayName = request.DisplayName?.Trim() ?? user.DisplayName;
        user.Role = request.Role ?? user.Role;
        user.OnboardingCompleted = request.OnboardingCompleted ?? user.OnboardingCompleted;
        await users.UpdateAsync(user);
        return TypedResults.Ok(await users.ToDtoAsync(user));
    }

    /// <summary>
    /// Deletes the account and everything it owns. Most rows go by database cascade from the user (synced data,
    /// tokens, logins, classes they teach with their members and assignments); the user's own memberships and
    /// assignment completions in other teachers' classes are removed first (see ClassConfiguration).
    /// </summary>
    private static async Task<NoContent> DeleteAsync(ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.ClassMemberships.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.AssignmentCompletions.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        return TypedResults.NoContent();
    }
}
