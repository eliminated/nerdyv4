using Nerdy.Core.Time;

namespace Nerdy.Core.Scheduling;

public enum ScheduleScope
{
    /// <summary>A single session on the chosen day of the week.</summary>
    Once,
    /// <summary>One session on each chosen day for the rest of the week.</summary>
    Week,
    /// <summary>Repeats on the chosen weekdays through the end of the month.</summary>
    Month
}

public enum PlanStatus { Ok, Moved, Skipped, Conflict }

public enum ConflictKind
{
    /// <summary>Overlaps an existing event or another session of the same block.</summary>
    Overlap,
    /// <summary>The start time has already passed.</summary>
    InPast,
    /// <summary>Starts before or ends after the study day window.</summary>
    OutsideStudyDay
}

/// <summary>An existing commitment on a day (local time).</summary>
public sealed record BusySlot(DateTime Start, DateTime End, string Title);

public sealed record StudyBlockRequest
{
    public required ScheduleScope Scope { get; init; }
    public required IReadOnlyCollection<DayOfWeek> Days { get; init; }
    public required TimeOnly Start { get; init; }
    public required TimeSpan Duration { get; init; }
    public string Title { get; init; } = "Study block";
    /// <summary>Any date inside the week (Once/Week) or month (Month) to plan; defaults to today.</summary>
    public DateOnly? Anchor { get; init; }
}

/// <summary>A per-date user decision made while resolving clashes.</summary>
public abstract record SessionOverride
{
    public sealed record Skip : SessionOverride;
    public sealed record MoveTo(DateOnly Date, TimeOnly Start) : SessionOverride;
}

public sealed record SlotSuggestion(DateOnly Date, TimeOnly Start);

/// <summary>
/// The outcome for one target date. <see cref="Key"/> is the originally targeted date (used for overrides);
/// <see cref="Date"/>/<see cref="Start"/> are where the session actually lands.
/// </summary>
public sealed record PlannedSession
{
    public required DateOnly Key { get; init; }
    public required DateOnly Date { get; init; }
    public required TimeOnly Start { get; init; }
    public required TimeSpan Duration { get; init; }
    public required PlanStatus Status { get; init; }
    public ConflictKind? Conflict { get; init; }
    /// <summary>The event this session overlaps, when <see cref="Conflict"/> is <see cref="ConflictKind.Overlap"/>.</summary>
    public BusySlot? ConflictWith { get; init; }
    /// <summary>No free slot was left on the day itself; suggestions are on other days.</summary>
    public bool DayFull { get; init; }
    /// <summary>No free slot was found for the rest of the week; the suggestion (if any) is a week later.</summary>
    public bool WeekFull { get; init; }
    public IReadOnlyList<SlotSuggestion> Suggestions { get; init; } = [];

    public DateTime StartDateTime => Date.ToDateTime(Start);
    public DateTime EndDateTime => StartDateTime + Duration;
    public bool WillBeAdded => Status is PlanStatus.Ok or PlanStatus.Moved;
}

/// <summary>The window of the day in which study sessions may be placed and suggested.</summary>
public sealed record StudyDay
{
    public TimeOnly Start { get; init; } = new(8, 0);
    public TimeOnly End { get; init; } = new(20, 0);
    public TimeSpan SlotStep { get; init; } = TimeSpan.FromMinutes(30);

    public static StudyDay Default { get; } = new();
}

/// <summary>
/// Plans a recurring study block against the user's calendar: decides which dates it lands on, detects
/// clashes (with existing events, with earlier sessions of the same block, with the past and with the
/// study-day window) and proposes nearby free slots: the two closest on the same day, otherwise the
/// first free slot on each of the next days, otherwise the same weekday next week.
/// </summary>
public sealed class SchedulePlanner(Func<DateOnly, IEnumerable<BusySlot>> busyOn, StudyDay? studyDay = null)
{
    private readonly StudyDay _day = studyDay ?? StudyDay.Default;

    /// <summary>The dates the block targets before clash resolution (never in the past).</summary>
    public IReadOnlyList<DateOnly> TargetDates(StudyBlockRequest request, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var anchor = request.Anchor ?? today;
        var days = request.Days.Distinct().OrderBy(WeekDays.Index).ToList();
        if (days.Count == 0) return [];

        IEnumerable<DateOnly> dates;
        switch (request.Scope)
        {
            case ScheduleScope.Month:
                var first = new DateOnly(anchor.Year, anchor.Month, 1);
                var from = first > today ? first : today;
                var to = WeekDays.EndOfMonth(anchor);
                dates = Enumerable.Range(0, Math.Max(0, to.DayNumber - from.DayNumber + 1))
                    .Select(from.AddDays)
                    .Where(d => days.Contains(d.DayOfWeek));
                break;
            case ScheduleScope.Once:
                var weekStart = WeekDays.StartOfWeek(anchor);
                dates = [weekStart.AddDays(WeekDays.Index(days[0]))];
                break;
            default:
                var ws = WeekDays.StartOfWeek(anchor);
                dates = days.Select(d => ws.AddDays(WeekDays.Index(d)));
                break;
        }
        return dates.Where(d => d >= today).Order().ToList();
    }

    public IReadOnlyList<PlannedSession> Plan(
        StudyBlockRequest request,
        IReadOnlyDictionary<DateOnly, SessionOverride>? overrides,
        DateTime now)
    {
        overrides ??= new Dictionary<DateOnly, SessionOverride>();
        var duration = request.Duration;
        var today = DateOnly.FromDateTime(now);
        // Sessions of this block that were placed earlier in the loop also count as busy.
        var placed = new Dictionary<DateOnly, List<BusySlot>>();

        IEnumerable<BusySlot> Busy(DateOnly d) =>
            placed.TryGetValue(d, out var p) ? busyOn(d).Concat(p) : busyOn(d);

        BusySlot? HitAt(DateOnly d, TimeOnly start)
        {
            var s = d.ToDateTime(start);
            var e = s + duration;
            return Busy(d).Where(b => s < b.End && e > b.Start).OrderBy(b => b.Start).FirstOrDefault();
        }

        bool InWindow(TimeOnly start) =>
            start >= _day.Start && start.ToTimeSpan() + duration <= _day.End.ToTimeSpan();

        bool IsPast(DateOnly d, TimeOnly start) => d < today || (d == today && d.ToDateTime(start) < now);

        bool Fits(DateOnly d, TimeOnly start) => InWindow(start) && !IsPast(d, start) && HitAt(d, start) is null;

        IEnumerable<TimeOnly> FreeSlots(DateOnly d, TimeOnly wanted)
        {
            var slots = new List<TimeOnly>();
            for (var t = _day.Start.ToTimeSpan(); t + duration <= _day.End.ToTimeSpan(); t += _day.SlotStep)
            {
                var start = TimeOnly.FromTimeSpan(t);
                if (Fits(d, start)) slots.Add(start);
            }
            // TimeOnly subtraction wraps around midnight, so compare as TimeSpans.
            return slots.OrderBy(s => Math.Abs((s.ToTimeSpan() - wanted.ToTimeSpan()).TotalMinutes))
                        .ThenBy(s => s);
        }

        var result = new List<PlannedSession>();
        foreach (var key in TargetDates(request, now))
        {
            overrides.TryGetValue(key, out var ov);
            if (ov is SessionOverride.Skip)
            {
                result.Add(Session(key, key, request.Start, PlanStatus.Skipped));
                continue;
            }

            var (date, start) = ov is SessionOverride.MoveTo m ? (m.Date, m.Start) : (key, request.Start);
            ConflictKind? kind = null;
            BusySlot? hit = null;
            if (IsPast(date, start)) kind = ConflictKind.InPast;
            else if (!InWindow(start)) kind = ConflictKind.OutsideStudyDay;
            else if ((hit = HitAt(date, start)) is not null) kind = ConflictKind.Overlap;

            if (kind is null)
            {
                var s = date.ToDateTime(start);
                (placed.TryGetValue(date, out var list) ? list : placed[date] = []).Add(new BusySlot(s, s + duration, request.Title));
                result.Add(Session(key, date, start, ov is null ? PlanStatus.Ok : PlanStatus.Moved));
                continue;
            }

            var suggestions = FreeSlots(date, start).Take(2).Select(t => new SlotSuggestion(date, t)).ToList();
            var dayFull = suggestions.Count == 0;
            var weekFull = false;
            if (dayFull)
            {
                for (var k = 1; k <= 6 && suggestions.Count < 2; k++)
                {
                    var next = date.AddDays(k);
                    var slot = FreeSlots(next, start).Cast<TimeOnly?>().FirstOrDefault();
                    if (slot is { } t) suggestions.Add(new SlotSuggestion(next, t));
                }
                if (suggestions.Count == 0)
                {
                    weekFull = true;
                    var nextWeek = date.AddDays(7);
                    var slot = FreeSlots(nextWeek, start).Cast<TimeOnly?>().FirstOrDefault();
                    if (slot is { } t) suggestions.Add(new SlotSuggestion(nextWeek, t));
                }
            }

            result.Add(Session(key, date, start, PlanStatus.Conflict) with
            {
                Conflict = kind,
                ConflictWith = hit,
                DayFull = dayFull,
                WeekFull = weekFull,
                Suggestions = suggestions
            });
        }
        return result;

        PlannedSession Session(DateOnly key, DateOnly date, TimeOnly start, PlanStatus status) => new()
        {
            Key = key,
            Date = date,
            Start = start,
            Duration = duration,
            Status = status
        };
    }

    /// <summary>Resolves every remaining clash with its first suggestion (or skips it when there is none).</summary>
    public static Dictionary<DateOnly, SessionOverride> WithFirstSuggestions(
        IEnumerable<PlannedSession> plan, IReadOnlyDictionary<DateOnly, SessionOverride>? overrides)
    {
        var result = new Dictionary<DateOnly, SessionOverride>(overrides ?? new Dictionary<DateOnly, SessionOverride>());
        foreach (var s in plan.Where(s => s.Status == PlanStatus.Conflict))
            result[s.Key] = s.Suggestions.Count > 0
                ? new SessionOverride.MoveTo(s.Suggestions[0].Date, s.Suggestions[0].Start)
                : new SessionOverride.Skip();
        return result;
    }

    /// <summary>Skips every remaining clash.</summary>
    public static Dictionary<DateOnly, SessionOverride> WithConflictsSkipped(
        IEnumerable<PlannedSession> plan, IReadOnlyDictionary<DateOnly, SessionOverride>? overrides)
    {
        var result = new Dictionary<DateOnly, SessionOverride>(overrides ?? new Dictionary<DateOnly, SessionOverride>());
        foreach (var s in plan.Where(s => s.Status == PlanStatus.Conflict))
            result[s.Key] = new SessionOverride.Skip();
        return result;
    }
}
