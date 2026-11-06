using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Classes;

/// <summary>The teacher's progress view: assignment completion for everyone, focus numbers only for students who opted in.</summary>
internal static class ClassProgressEndpoints
{
    private const int MaxRangeDays = 120;
    private const int DefaultRangeDays = 7;
    private const int MaxUtcOffsetMinutes = 14 * 60;

    public static RouteGroupBuilder MapClassProgressEndpoints(this RouteGroupBuilder classes)
    {
        classes.MapGet("/{id:guid}/progress", GetAsync);
        return classes;
    }

    /// <param name="from">Range start (inclusive); defaults to 7 days before <paramref name="to"/>.</param>
    /// <param name="to">Range end (exclusive); defaults to now.</param>
    /// <param name="utcOffsetMinutes">The teacher's UTC offset, used to bucket <see cref="StudentProgressDto.DailyMinutes"/> into local days.</param>
    private static async Task<Results<Ok<ClassProgressDto>, ValidationProblem, ProblemHttpResult>> GetAsync(
        Guid id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? utcOffsetMinutes,
        ClaimsPrincipal principal,
        NerdyDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
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

        var end = to ?? time.GetUtcNow();
        var start = from ?? end.AddDays(-DefaultRangeDays);
        var offsetMinutes = utcOffsetMinutes ?? 0;
        var errors = new ValidationErrors();
        if (start >= end)
        {
            errors.Add("from", "Must be before 'to'.");
        }
        else if (end - start > TimeSpan.FromDays(MaxRangeDays))
        {
            errors.Add("to", $"The range can be at most {MaxRangeDays} days.");
        }

        errors.Range("utcOffsetMinutes", offsetMinutes, -MaxUtcOffsetMinutes, MaxUtcOffsetMinutes);
        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var students = await db.ClassMemberships
            .Where(m => m.ClassroomId == id && m.Role == ClassRole.Student)
            .Select(m => new { m.UserId, m.User.DisplayName, m.SharesFocus })
            .ToListAsync(cancellationToken);

        var assignmentsTotal = await db.Assignments.CountAsync(a => a.ClassroomId == id, cancellationToken);
        var assignmentsDone = await db.AssignmentCompletions
            .Where(c => c.Assignment.ClassroomId == id && c.IsDone)
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);

        // Only sessions of students who share with this class are ever read, and only those started since they began sharing.
        var sessions = await db.FocusSessions
            .Where(f => !f.IsDeleted && f.StartedAt >= start && f.StartedAt < end
                && db.ClassMemberships.Any(m => m.ClassroomId == id && m.UserId == f.UserId && m.Role == ClassRole.Student
                    && m.SharesFocus && m.SharesFocusSince != null && f.StartedAt >= m.SharesFocusSince))
            .Select(f => new { f.UserId, f.StartedAt, f.ActualSeconds, f.Outcome })
            .ToListAsync(cancellationToken);
        var sessionsByStudent = sessions.ToLookup(s => s.UserId);

        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var firstDay = LocalDay(start, offset);
        var dayCount = LocalDay(end.AddTicks(-1), offset).DayNumber - firstDay.DayNumber + 1;

        var progress = students
            .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(student =>
            {
                var done = assignmentsDone.GetValueOrDefault(student.UserId);
                if (!student.SharesFocus)
                {
                    return new StudentProgressDto
                    {
                        UserId = student.UserId,
                        DisplayName = student.DisplayName,
                        AssignmentsDone = done,
                        AssignmentsTotal = assignmentsTotal,
                    };
                }

                var own = sessionsByStudent[student.UserId].ToList();
                var dailySeconds = new long[dayCount];
                foreach (var session in own)
                {
                    dailySeconds[LocalDay(session.StartedAt, offset).DayNumber - firstDay.DayNumber] += session.ActualSeconds;
                }

                return new StudentProgressDto
                {
                    UserId = student.UserId,
                    DisplayName = student.DisplayName,
                    SharesFocus = true,
                    FocusMinutes = (int)(own.Sum(s => (long)s.ActualSeconds) / 60),
                    Sessions = own.Count,
                    CompletedAsPlannedPercent = own.Count == 0
                        ? null
                        : (int)Math.Round(100.0 * own.Count(s => s.Outcome == FocusOutcome.Completed) / own.Count),
                    DailyMinutes = dailySeconds.Select(seconds => (int)(seconds / 60)).ToList(),
                    AssignmentsDone = done,
                    AssignmentsTotal = assignmentsTotal,
                };
            })
            .ToList();

        return TypedResults.Ok(new ClassProgressDto { ClassroomId = id, From = start, To = end, Students = progress });
    }

    private static DateOnly LocalDay(DateTimeOffset instant, TimeSpan offset) => DateOnly.FromDateTime(instant.ToOffset(offset).DateTime);
}
