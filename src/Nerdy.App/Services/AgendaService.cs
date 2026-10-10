using Nerdy.App.Services.Classes;
using Nerdy.App.Services.Data;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Scheduling;
using Nerdy.Core.Stats;
using Nerdy.Core.Time;

namespace Nerdy.App.Services;

public enum OccurrenceSource { Event, Assignment }

/// <summary>A concrete calendar entry (an event occurrence or a class assignment deadline), in local time.</summary>
public sealed record AgendaItem
{
    public required Guid Id { get; init; }
    public required OccurrenceSource Source { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public Guid? TopicId { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public bool IsAllDay { get; init; }
    public bool IsMilestone { get; init; }
    /// <summary>Overrides the topic colour (class colour for assignments).</summary>
    public string? ColorHex { get; init; }
    public bool AutoFocus { get; init; }
    public FocusMode AutoFocusMode { get; init; }
    public int? ReminderMinutes { get; init; }
    public bool IsRecurring { get; init; }

    public DateOnly Date => DateOnly.FromDateTime(Start);
    public double StartHour => Start.TimeOfDay.TotalHours;
    public double EndHour => End.Date > Start.Date ? 24 : End.TimeOfDay.TotalHours;
}

/// <summary>Calendar and progress queries shared by Home, Calendar, Schedule, Topics and notifications.</summary>
public sealed class AgendaService(DataService data, ClassesService classes, IClock clock)
{
    public DateTime Now => clock.LocalNow;
    public DateOnly Today => clock.Today;

    /// <summary>Everything overlapping [from, to), sorted by start.</summary>
    public IReadOnlyList<AgendaItem> Between(DateTime from, DateTime to)
    {
        var items = new List<AgendaItem>();
        foreach (var e in data.Events)
        {
            var start = e.StartsAt.LocalDateTime;
            var end = e.EndsAt.LocalDateTime;
            foreach (var o in Recurrence.Expand(start, end, e.RepeatDays, e.RepeatUntil, from, to))
                items.Add(new AgendaItem
                {
                    Id = e.Id,
                    Source = OccurrenceSource.Event,
                    Title = e.Title,
                    Subtitle = e.Subtitle,
                    TopicId = e.TopicId,
                    Start = o.Start,
                    End = o.End,
                    IsAllDay = e.IsAllDay,
                    IsMilestone = e.IsMilestone,
                    AutoFocus = e.AutoFocus,
                    AutoFocusMode = e.AutoFocusMode,
                    ReminderMinutes = e.ReminderMinutes,
                    IsRecurring = e.RepeatDays != 0
                });
        }

        foreach (var a in classes.MyAssignments.Where(a => !a.IsDone))
        {
            var due = a.DueAt.LocalDateTime;
            if (due < from || due >= to) continue;
            items.Add(new AgendaItem
            {
                Id = a.Id,
                Source = OccurrenceSource.Assignment,
                Title = a.Title,
                Subtitle = $"Due · {a.ClassroomName}",
                Start = due.AddMinutes(-30),
                End = due,
                IsMilestone = true,
                ColorHex = a.ClassColorHex
            });
        }

        return items.OrderBy(i => i.IsAllDay ? 0 : 1).ThenBy(i => i.Start).ToList();
    }

    public IReadOnlyList<AgendaItem> On(DateOnly date) =>
        Between(date.ToDateTime(TimeOnly.MinValue), date.AddDays(1).ToDateTime(TimeOnly.MinValue));

    /// <summary>Commitments that a new study block must not overlap (all-day markers don't block time).</summary>
    public IEnumerable<BusySlot> BusyOn(DateOnly date) =>
        On(date).Where(i => !i.IsAllDay && i.Source == OccurrenceSource.Event).Select(i => new BusySlot(i.Start, i.End, i.Title));

    /// <summary>The next thing on today's agenda that hasn't ended yet.</summary>
    public AgendaItem? NextToday() => On(Today).FirstOrDefault(i => !i.IsAllDay && i.End > Now);

    public AgendaItem? NextForTopic(Guid topicId) =>
        Between(Now, Now.AddDays(21)).FirstOrDefault(i => i.TopicId == topicId && i.Start >= Now);

    /// <summary>"Today 2:00 PM", "Sat 10:00 AM", or "Nothing planned".</summary>
    public string NextLabel(Guid topicId)
    {
        if (NextForTopic(topicId) is not { } next) return "Nothing planned";
        var day = next.Date == Today ? "Today" : next.Date == Today.AddDays(1) ? "Tomorrow" : next.Start.ToString("ddd");
        return $"{day} {Fmt.Clock(next.Start)}";
    }

    /// <summary>Average progress of a topic's subtopics (0–100).</summary>
    public int TopicProgress(Guid topicId)
    {
        var subs = data.SubtopicsOf(topicId);
        return subs.Count == 0 ? 0 : (int)Math.Round(subs.Average(s => s.Progress));
    }

    public string TopicColor(Guid? topicId) => data.Topic(topicId)?.ColorHex ?? "#64748B";
    public string TopicName(Guid? topicId) => data.Topic(topicId)?.Name ?? "General";

    /// <summary>Reminders due today or overdue (not done), plus today's class assignments, soonest first.</summary>
    public IReadOnlyList<ReminderDto> DueToday() =>
        data.Reminders
            .Where(r => !r.IsDone && r.DueAt is { } d && DateOnly.FromDateTime(d.LocalDateTime) <= Today)
            .OrderBy(r => r.DueAt)
            .ToList();

    /// <summary>Finished focus sessions as the stats engine sees them.</summary>
    public IReadOnlyList<SessionRecord> SessionRecords() =>
        data.Sessions.Select(s => new SessionRecord
        {
            Id = s.Id,
            TopicId = s.TopicId,
            Task = s.Task,
            Start = s.StartedAt.LocalDateTime,
            ActualMinutes = (int)Math.Round(s.ActualSeconds / 60.0),
            PlannedMinutes = (int)Math.Round(s.PlannedSeconds / 60.0),
            Mode = s.Mode,
            Outcome = s.Outcome,
            ExitReason = s.ExitReason,
            ExitNote = s.ExitNote
        }).ToList();
}
