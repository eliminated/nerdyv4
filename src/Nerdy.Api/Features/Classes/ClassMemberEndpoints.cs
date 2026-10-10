using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Classes;

/// <summary>Who is in a class, leaving / removing, and the student's focus-sharing opt-in.</summary>
internal static class ClassMemberEndpoints
{
    public static RouteGroupBuilder MapClassMemberEndpoints(this RouteGroupBuilder classes)
    {
        classes.MapGet("/{id:guid}/members", ListAsync);
        classes.MapDelete("/{id:guid}/members/{userId:guid}", RemoveAsync);
        classes.MapPut("/{id:guid}/sharing", SetSharingAsync);
        return classes;
    }

    /// <summary>Teachers see everyone with emails; students see the teacher(s) and themselves, without emails.</summary>
    private static async Task<Results<Ok<List<ClassMemberDto>>, ProblemHttpResult>> ListAsync(
        Guid id, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var membership = await db.FindMembershipAsync(id, userId, cancellationToken);
        if (membership is null)
        {
            return ClassProblems.ClassNotFound();
        }

        var isTeacher = membership.Role == ClassRole.Teacher;
        var members = await db.ClassMemberships
            .Where(m => m.ClassroomId == id && (isTeacher || m.Role == ClassRole.Teacher || m.UserId == userId))
            .OrderBy(m => m.Role == ClassRole.Teacher ? 0 : 1)
            .ThenBy(m => m.User.DisplayName)
            .Select(m => new ClassMemberDto
            {
                UserId = m.UserId,
                DisplayName = m.User.DisplayName,
                Email = isTeacher ? m.User.Email : null,
                Role = m.Role,
                JoinedAt = m.JoinedAt,
                SharesFocus = m.SharesFocus,
            })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(members);
    }

    /// <summary>Teachers remove any student; a student may remove only themself (leave). Their completions go too.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        Guid id, Guid userId, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var callerId = principal.GetUserId();
        var caller = await db.FindMembershipAsync(id, callerId, cancellationToken);
        if (caller is null)
        {
            return ClassProblems.ClassNotFound();
        }

        if (caller.Role != ClassRole.Teacher && userId != callerId)
        {
            return ClassProblems.TeacherOnly();
        }

        if (userId == caller.Classroom.OwnerId)
        {
            return ApiProblems.Conflict(ApiErrorCodes.OwnerCannotLeave,
                "The class's owner can't leave or be removed. Archive or delete the class instead.");
        }

        var target = await db.ClassMemberships.FindAsync([id, userId], cancellationToken);
        if (target is null)
        {
            return ApiProblems.NotFound("Member");
        }

        await db.AssignmentCompletions
            .Where(c => c.UserId == userId && c.Assignment.ClassroomId == id)
            .ExecuteDeleteAsync(cancellationToken);
        db.ClassMemberships.Remove(target);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ClassroomDto>, ProblemHttpResult>> SetSharingAsync(
        Guid id, SetFocusSharingRequest request, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var membership = await db.FindMembershipAsync(id, userId, cancellationToken);
        if (membership is null)
        {
            return ClassProblems.ClassNotFound();
        }

        if (membership.Role != ClassRole.Student)
        {
            return ClassProblems.StudentsOnly();
        }

        membership.SharesFocus = request.Share;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await db.GetClassroomAsync(id, userId, cancellationToken));
    }
}
