namespace Nerdy.Core.Time;

/// <summary>One concrete occurrence of a (possibly repeating) event, in local time.</summary>
public readonly record struct Occurrence(DateTime Start, DateTime End)
{
    public DateOnly Date => DateOnly.FromDateTime(Start);
}

public static class Recurrence
{
    /// <summary>
    /// Expands an event into the occurrences that overlap [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>).
    /// A repeating event recurs on every weekday in <paramref name="repeatDays"/> from its first date up to and
    /// including <paramref name="repeatUntil"/>, keeping the first occurrence's local start time and duration.
    /// </summary>
    public static IEnumerable<Occurrence> Expand(
        DateTime firstStart, DateTime firstEnd, int repeatDays, DateOnly? repeatUntil,
        DateTime rangeStart, DateTime rangeEnd)
    {
        if (firstEnd < firstStart) firstEnd = firstStart;
        var duration = firstEnd - firstStart;

        if (repeatDays == WeekDays.None)
        {
            if (firstStart < rangeEnd && firstEnd > rangeStart) yield return new Occurrence(firstStart, firstEnd);
            yield break;
        }

        var firstDate = DateOnly.FromDateTime(firstStart);
        var timeOfDay = firstStart.TimeOfDay;
        // An occurrence that started the day before can still overlap the range when it runs past midnight.
        var from = DateOnly.FromDateTime(rangeStart.AddDays(-Math.Ceiling(duration.TotalDays)));
        if (from < firstDate) from = firstDate;
        var to = DateOnly.FromDateTime(rangeEnd);
        if (repeatUntil is { } until && until < to) to = until;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (!WeekDays.Contains(repeatDays, d.DayOfWeek)) continue;
            var start = d.ToDateTime(TimeOnly.MinValue) + timeOfDay;
            var end = start + duration;
            if (start < rangeEnd && end > rangeStart) yield return new Occurrence(start, end);
        }
    }
}
