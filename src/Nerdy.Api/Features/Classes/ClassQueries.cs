using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Classes;

/// <summary>
/// Everything in the classes module is seen through the caller's <see cref="ClassMembership"/>: it decides
/// visibility (non-members get 404, so class ids don't leak) and which role-specific fields are filled in.
/// </summary>
internal static class ClassQueries
{
    public const string DefaultColorHex = "#5B5BD6";

    public static Task<ClassMembership?> FindMembershipAsync(
        this NerdyDbContext db, Guid classroomId, Guid userId, CancellationToken cancellationToken) =>
        db.ClassMemberships
            .Include(m => m.Classroom)
            .SingleOrDefaultAsync(m => m.ClassroomId == classroomId && m.UserId == userId, cancellationToken);

    public static IQueryable<ClassMembership> MembershipsOf(this NerdyDbContext db, Guid userId) =>
        db.ClassMemberships.Where(m => m.UserId == userId);

    public static IQueryable<ClassroomDto> SelectClassrooms(this IQueryable<ClassMembership> memberships) =>
        memberships.Select(m => new ClassroomDto
        {
            Id = m.ClassroomId,
            Name = m.Classroom.Name,
            Subject = m.Classroom.Subject,
            ColorHex = m.Classroom.ColorHex,
            JoinCode = m.Role == ClassRole.Teacher ? m.Classroom.JoinCode : null,
            MyRole = m.Role,
            TeacherName = m.Classroom.Owner.DisplayName,
            StudentCount = m.Classroom.Members.Count(s => s.Role == ClassRole.Student),
            SharesFocus = m.SharesFocus,
            IsArchived = m.Classroom.IsArchived,
            CreatedAt = m.Classroom.CreatedAt,
        });

    /// <summary>Assignments of the given memberships' classes; students get their own completion, teachers the count.</summary>
    public static IQueryable<AssignmentDto> SelectAssignments(this IQueryable<ClassMembership> memberships) =>
        memberships.SelectMany(m => m.Classroom.Assignments, (m, a) => new AssignmentDto
        {
            Id = a.Id,
            ClassroomId = a.ClassroomId,
            ClassroomName = m.Classroom.Name,
            ClassColorHex = m.Classroom.ColorHex,
            Title = a.Title,
            Description = a.Description,
            DueAt = a.DueAt,
            CreatedAt = a.CreatedAt,
            IsDone = m.Role == ClassRole.Student && a.Completions.Any(c => c.UserId == m.UserId && c.IsDone),
            CompletedAt = m.Role == ClassRole.Student
                ? a.Completions.Where(c => c.UserId == m.UserId && c.IsDone).Select(c => c.CompletedAt).FirstOrDefault()
                : null,
            CompletedCount = m.Role == ClassRole.Teacher ? a.Completions.Count(c => c.IsDone) : 0,
            StudentCount = m.Classroom.Members.Count(s => s.Role == ClassRole.Student),
        });

    public static Task<ClassroomDto> GetClassroomAsync(
        this NerdyDbContext db, Guid classroomId, Guid userId, CancellationToken cancellationToken) =>
        db.MembershipsOf(userId).Where(m => m.ClassroomId == classroomId).SelectClassrooms().SingleAsync(cancellationToken);

    public static Task<AssignmentDto> GetAssignmentAsync(
        this NerdyDbContext db, Guid classroomId, Guid assignmentId, Guid userId, CancellationToken cancellationToken) =>
        db.MembershipsOf(userId).Where(m => m.ClassroomId == classroomId).SelectAssignments()
            .SingleAsync(a => a.Id == assignmentId, cancellationToken);

    /// <summary>Validates the editable classroom fields; <paramref name="name"/> is required unless this is a PATCH that omits it.</summary>
    public static ValidationErrors ValidateClassroom(string? name, string? subject, string? colorHex, bool nameRequired)
    {
        var errors = new ValidationErrors();
        if (nameRequired || name is not null)
        {
            errors.Required("name", name, FieldLimits.Name);
        }

        errors.MaxLength("subject", subject, FieldLimits.Name);
        errors.ColorHex("colorHex", colorHex);
        return errors;
    }

    /// <summary>Trims optional text; blank becomes null so it can be cleared through PATCH with "".</summary>
    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class ClassProblems
{
    public static ProblemHttpResult ClassNotFound() => ApiProblems.NotFound("Class");

    public static ProblemHttpResult AssignmentNotFound() => ApiProblems.NotFound("Assignment");

    public static ProblemHttpResult TeacherOnly() =>
        ApiProblems.Forbidden(ApiErrorCodes.NotClassTeacher, "Only this class's teacher can do that.");

    public static ProblemHttpResult StudentsOnly() =>
        ApiProblems.Forbidden(ApiErrorCodes.StudentsOnly, "Only students of this class can do that.");

    public static ProblemHttpResult Archived() =>
        ApiProblems.Conflict(ApiErrorCodes.ClassArchived, "This class is archived and read-only.");
}
