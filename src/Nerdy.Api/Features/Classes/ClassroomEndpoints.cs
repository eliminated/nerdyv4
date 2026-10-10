using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Classes;

/// <summary>Creating, joining and managing classes.</summary>
internal static class ClassroomEndpoints
{
    public static RouteGroupBuilder MapClassroomEndpoints(this RouteGroupBuilder classes)
    {
        classes.MapGet("", ListAsync);
        classes.MapPost("", CreateAsync);
        classes.MapPost("/join", JoinAsync);
        classes.MapGet("/{id:guid}", GetAsync);
        classes.MapPatch("/{id:guid}", UpdateAsync);
        classes.MapDelete("/{id:guid}", DeleteAsync);
        classes.MapPost("/{id:guid}/join-code", RegenerateJoinCodeAsync);
        return classes;
    }

    /// <summary>Classes I teach or joined, archived ones included (flagged).</summary>
    private static async Task<Ok<List<ClassroomDto>>> ListAsync(ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await db.MembershipsOf(principal.GetUserId())
            .SelectClassrooms()
            .OrderBy(c => c.IsArchived)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<ClassroomDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateClassroomRequest request, ClaimsPrincipal principal, NerdyDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var role = await db.Users.Where(u => u.Id == userId).Select(u => (UserRole?)u.Role).SingleOrDefaultAsync(cancellationToken);
        if (role != UserRole.Teacher)
        {
            return ApiProblems.Forbidden(ApiErrorCodes.TeacherRequired,
                "Only teachers can create classes. Set your role to Teacher in your profile first.");
        }

        var errors = ClassQueries.ValidateClassroom(request.Name, request.Subject, request.ColorHex, nameRequired: true);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var now = time.GetUtcNow();
        var classroom = new Classroom
        {
            OwnerId = userId,
            Name = request.Name.Trim(),
            Subject = ClassQueries.NullIfBlank(request.Subject),
            ColorHex = request.ColorHex ?? ClassQueries.DefaultColorHex,
            JoinCode = await JoinCodes.NewUniqueAsync(db, cancellationToken),
            CreatedAt = now,
        };
        classroom.Members.Add(new ClassMembership { UserId = userId, Role = ClassRole.Teacher, JoinedAt = now });
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/classes/{classroom.Id}", await db.GetClassroomAsync(classroom.Id, userId, cancellationToken));
    }

    /// <summary>Idempotent: joining a class you are already a student of just returns it.</summary>
    private static async Task<Results<Ok<ClassroomDto>, ProblemHttpResult>> JoinAsync(
        JoinClassroomRequest request, ClaimsPrincipal principal, NerdyDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var code = JoinCodes.Normalize(request.JoinCode);
        var classroom = code.Length == JoinCodes.Length
            ? await db.Classrooms.SingleOrDefaultAsync(c => c.JoinCode == code, cancellationToken)
            : null;
        if (classroom is null)
        {
            return ApiProblems.Problem(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "No class has that join code.");
        }

        var membership = await db.ClassMemberships.FindAsync([classroom.Id, userId], cancellationToken);
        if (membership is { Role: ClassRole.Teacher })
        {
            return ApiProblems.Conflict(ApiErrorCodes.AlreadyTeacher, "You teach this class, so you can't join it as a student.");
        }

        if (membership is null)
        {
            if (classroom.IsArchived)
            {
                return ClassProblems.Archived();
            }

            db.ClassMemberships.Add(new ClassMembership
            {
                ClassroomId = classroom.Id,
                UserId = userId,
                Role = ClassRole.Student,
                JoinedAt = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await db.GetClassroomAsync(classroom.Id, userId, cancellationToken));
    }

    private static async Task<Results<Ok<ClassroomDto>, ProblemHttpResult>> GetAsync(
        Guid id, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var classroom = await db.MembershipsOf(principal.GetUserId())
            .Where(m => m.ClassroomId == id)
            .SelectClassrooms()
            .SingleOrDefaultAsync(cancellationToken);
        return classroom is null ? ClassProblems.ClassNotFound() : TypedResults.Ok(classroom);
    }

    private static async Task<Results<Ok<ClassroomDto>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateClassroomRequest request, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
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

        var errors = ClassQueries.ValidateClassroom(request.Name, request.Subject, request.ColorHex, nameRequired: false);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var classroom = membership.Classroom;
        classroom.Name = request.Name?.Trim() ?? classroom.Name;
        classroom.Subject = request.Subject is null ? classroom.Subject : ClassQueries.NullIfBlank(request.Subject);
        classroom.ColorHex = request.ColorHex ?? classroom.ColorHex;
        classroom.IsArchived = request.IsArchived ?? classroom.IsArchived;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await db.GetClassroomAsync(id, userId, cancellationToken));
    }

    /// <summary>Deletes the class with its memberships, assignments and completions (database cascade).</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
    {
        var membership = await db.FindMembershipAsync(id, principal.GetUserId(), cancellationToken);
        if (membership is null)
        {
            return ClassProblems.ClassNotFound();
        }

        if (membership.Role != ClassRole.Teacher)
        {
            return ClassProblems.TeacherOnly();
        }

        await db.Classrooms.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Replaces the join code, e.g. after it leaked; existing members stay.</summary>
    private static async Task<Results<Ok<ClassroomDto>, ProblemHttpResult>> RegenerateJoinCodeAsync(
        Guid id, ClaimsPrincipal principal, NerdyDbContext db, CancellationToken cancellationToken)
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

        membership.Classroom.JoinCode = await JoinCodes.NewUniqueAsync(db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await db.GetClassroomAsync(id, userId, cancellationToken));
    }
}
