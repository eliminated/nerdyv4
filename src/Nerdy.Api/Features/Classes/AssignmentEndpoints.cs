using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Classes;

/// <summary>Assignments a teacher sets for a class, and students' completion of them.</summary>
internal static class AssignmentEndpoints
{
    public static RouteGroupBuilder MapClassAssignmentEndpoints(this RouteGroupBuilder classes)
    {
        classes.MapGet("/{id:guid}/assignments", ListForClassAsync);
        classes.MapPost("/{id:guid}/assignments", CreateAsync);
        return classes;
    }

    public static RouteGroupBuilder MapAssignmentEndpoints(this RouteGroupBuilder assignments)
    {
        assignments.MapGet("", ListMineAsync);
        assignments.MapPatch("/{id:guid}", UpdateAsync);
        assignments.MapDelete("/{id:guid}", DeleteAsync);
        assignments.MapPut("/{id:guid}/done", SetDoneAsync);
        return assignments;
    }

    private static async Task<Results<Ok<List<AssignmentDto>>, ProblemHttpResult>> ListForClassAsync(
        Guid id, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var membership = db.MembershipsOf(principal.GetUserId()).Where(m => m.ClassroomId == id);
        if (!await membership.AnyAsync(cancellationToken))
        {
            return ClassProblems.ClassNotFound();
        }

        return TypedResults.Ok(await membership.SelectAssignments()
            .OrderBy(a => a.DueAt).ThenBy(a => a.Title)
            .ToListAsync(cancellationToken));
    }

    /// <summary>Everything due in the classes I'm a student of (archived classes excluded); feeds Reminders and Calendar.</summary>
    private static async Task<Ok<List<AssignmentDto>>> ListMineAsync(ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await db.MembershipsOf(principal.GetUserId())
            .Where(m => m.Role == ClassRole.Student && !m.Classroom.IsArchived)
            .SelectAssignments()
            .OrderBy(a => a.DueAt).ThenBy(a => a.Title)
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<AssignmentDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        Guid id, CreateAssignmentRequest request, ClaimsPrincipal principal, NerdyDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var membership = await db.FindMembershipAsync(id, userId, cancellationToken);
        if (membership is null)
        {
            return ClassProblems.ClassNotFound();
        }

        if (membership.Role != ClassRole.Teacher)
        {
            return ClassProblems.TeacherOnly();
        }

        if (membership.Classroom.IsArchived)
        {
            return ClassProblems.Archived();
        }

        var errors = Validate(request.Title, request.Description, titleRequired: true);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var assignment = new Assignment
        {
            ClassroomId = id,
            Title = request.Title.Trim(),
            Description = ClassQueries.NullIfBlank(request.Description),
            DueAt = request.DueAt,
            CreatedAt = time.GetUtcNow(),
            CreatedById = userId,
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/assignments/{assignment.Id}",
            await db.GetAssignmentAsync(id, assignment.Id, userId, cancellationToken));
    }

    private static async Task<Results<Ok<AssignmentDto>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateAssignmentRequest request, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var (assignment, membership) = await FindAsync(db, id, userId, cancellationToken);
        if (assignment is null || membership is null)
        {
            return ClassProblems.AssignmentNotFound();
        }

        if (membership.Role != ClassRole.Teacher)
        {
            return ClassProblems.TeacherOnly();
        }

        if (assignment.Classroom.IsArchived)
        {
            return ClassProblems.Archived();
        }

        var errors = Validate(request.Title, request.Description, titleRequired: false);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        assignment.Title = request.Title?.Trim() ?? assignment.Title;
        assignment.Description = request.Description is null ? assignment.Description : ClassQueries.NullIfBlank(request.Description);
        assignment.DueAt = request.DueAt ?? assignment.DueAt;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await db.GetAssignmentAsync(assignment.ClassroomId, id, userId, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var (assignment, membership) = await FindAsync(db, id, principal.GetUserId(), cancellationToken);
        if (assignment is null || membership is null)
        {
            return ClassProblems.AssignmentNotFound();
        }

        if (membership.Role != ClassRole.Teacher)
        {
            return ClassProblems.TeacherOnly();
        }

        if (assignment.Classroom.IsArchived)
        {
            return ClassProblems.Archived();
        }

        db.Assignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AssignmentDto>, ProblemHttpResult>> SetDoneAsync(
        Guid id, SetAssignmentDoneRequest request, ClaimsPrincipal principal, NerdyDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var (assignment, membership) = await FindAsync(db, id, userId, cancellationToken);
        if (assignment is null || membership is null)
        {
            return ClassProblems.AssignmentNotFound();
        }

        if (membership.Role != ClassRole.Student)
        {
            return ClassProblems.StudentsOnly();
        }

        if (assignment.Classroom.IsArchived)
        {
            return ClassProblems.Archived();
        }

        var completion = await db.AssignmentCompletions.FindAsync([id, userId], cancellationToken);
        if (completion is null)
        {
            completion = new AssignmentCompletion { AssignmentId = id, UserId = userId };
            db.AssignmentCompletions.Add(completion);
        }

        if (completion.IsDone != request.IsDone)
        {
            completion.IsDone = request.IsDone;
            completion.CompletedAt = request.IsDone ? time.GetUtcNow() : null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await db.GetAssignmentAsync(assignment.ClassroomId, id, userId, cancellationToken));
    }

    /// <summary>The assignment with its class, and the caller's membership of that class (null when not a member).</summary>
    private static async Task<(Assignment? Assignment, ClassMembership? Membership)> FindAsync(
        NerdyDbContext db, Guid assignmentId, Guid userId, CancellationToken cancellationToken)
    {
        var assignment = await db.Assignments.Include(a => a.Classroom)
            .SingleOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        var membership = assignment is null
            ? null
            : await db.ClassMemberships.FindAsync([assignment.ClassroomId, userId], cancellationToken);
        return (assignment, membership);
    }

    private static ValidationErrors Validate(string? title, string? description, bool titleRequired)
    {
        var errors = new ValidationErrors();
        if (titleRequired || title is not null)
        {
            errors.Required("title", title, FieldLimits.Name);
        }

        errors.MaxLength("description", description, FieldLimits.AssignmentDescription);
        return errors;
    }
}
