using Nerdy.Contracts;
using Nerdy.Core.Time;

namespace Nerdy.Core.Stats;

public enum StatsRange
{
    /// <summary>The last 7 days including today, one bar per day.</summary>
    Week,
    /// <summary>The last 30 days including today, one bar per day.</summary>
    Month,
    /// <summary>The last 8 weeks including today, one bar per week.</summary>
    Term
}

public enum HistoryFilter { All, Focus, Ultra, EndedEarly }

/// <summary>A finished focus session as the stats engine sees it (local time).</summary>
public sealed record SessionRecord
{
    public required Guid Id { get; init; }
    public Guid? TopicId { get; init; }
    public string Task { get; init; } = "";
    public required DateTime Start { get; init; }
    public required int ActualMinutes { get; init; }
    public required int PlannedMinutes { get; init; }
    public required FocusMode Mode { get; init; }
    public required FocusOutcome Outcome { get; init; }
    public ExitReason? ExitReason { get; init; }
    public string? ExitNote { get; init; }

    public DateOnly Date => DateOnly.FromDateTime(Start);
    public bool RanAsPlanned => Outcome == FocusOutcome.Completed;
}

public sealed record TopicInfo(Guid Id, string Name, string ColorHex);

public enum DeltaUnit { Percent, Points }

/// <summary>A headline number with its change vs the previous period (null when there is nothing to compare).</summary>
public sealed record Kpi(double Value, int? Delta, DeltaUnit Unit);

public sealed record TopicMinutes(Guid? TopicId, int Minutes);

public sealed record StatsBar(DateOnly Start, DateOnly End, int TotalMinutes, IReadOnlyList<TopicMinutes> Segments)
{
    public bool IsCurrent { get; init; }
}

public sealed record TopicTime(Guid? TopicId, int Minutes, int Percent, int Sessions, int AverageMinutes);

public sealed record PeakWindow(int StartHour, int SharePercent);
public sealed record StrongestDay(DayOfWeek Day, double AverageMinutes);
public sealed record EarlyEnder(Guid? TopicId, int EndedEarly, int Sessions);
public sealed record Streak(int Current, int Best);

public sealed record UltraSummary(int Held, int Total, IReadOnlyList<SessionRecord> RecentExits);

public sealed record StatsReport
{
    public required StatsRange Range { get; init; }
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }

    public required Kpi FocusMinutes { get; init; }
    public required Kpi SessionCount { get; init; }
    public required Kpi AverageMinutes { get; init; }
    /// <summary>0–100.</summary>
    public required Kpi CompletedAsPlanned { get; init; }

    public required IReadOnlyList<StatsBar> Bars { get; init; }
    /// <summary>Top of the bar chart's axis and the gridline step, both in minutes.</summary>
    public required int AxisMaxMinutes { get; init; }
    public required int AxisStepMinutes { get; init; }

    public required IReadOnlyList<TopicTime> ByTopic { get; init; }
    public required int TotalMinutes { get; init; }

    /// <summary>[weekday index Monday=0, hour column] → minutes, for sessions starting inside the study day.</summary>
    public required int[,] Heatmap { get; init; }
    public required int HeatmapStartHour { get; init; }
    public required int HeatmapMax { get; init; }

    public PeakWindow? Peak { get; init; }
    public StrongestDay? Strongest { get; init; }
    public EarlyEnder? EarlyEnder { get; init; }
    public required Streak Streak { get; init; }
    public required UltraSummary Ultra { get; init; }

    /// <summary>Sessions in range, newest first.</summary>
    public required IReadOnlyList<SessionRecord> Sessions { get; init; }
}

/// <summary>Computes the Statistics &amp; History screen and the home-page focus widgets.</summary>
public static class FocusStats
{
    public static int Days(StatsRange range) => range switch
    {
        StatsRange.Week => 7,
        StatsRange.Month => 30,
        _ => 56
    };

    public static StatsReport Build(
        IReadOnlyCollection<SessionRecord> all, StatsRange range, DateOnly today,
        int studyDayStartHour = 8, int studyDayEndHour = 20)
    {
        var n = Days(range);
        var from = today.AddDays(-(n - 1));
        var prevFrom = from.AddDays(-n);
        var cur = all.Where(s => s.Date >= from && s.Date <= today).ToList();
        var prev = all.Where(s => s.Date >= prevFrom && s.Date < from).ToList();

        static int Sum(IEnumerable<SessionRecord> l) => l.Sum(s => s.ActualMinutes);
        static double Rate(IReadOnlyCollection<SessionRecord> l) => l.Count == 0 ? 0 : (double)l.Count(s => s.RanAsPlanned) / l.Count;
        static double Avg(IReadOnlyCollection<SessionRecord> l) => l.Count == 0 ? 0 : (double)Sum(l) / l.Count;
        static int? Pct(double x, double y) => y == 0 ? null : (int)Math.Round((x - y) / y * 100);

        var total = Sum(cur);

        // Bars: one per day, or one per week for the term view.
        var groups = range == StatsRange.Term
            ? Enumerable.Range(0, 8).Select(i => (Start: from.AddDays(i * 7), End: from.AddDays(i * 7 + 6)))
            : Enumerable.Range(0, n).Select(i => (Start: from.AddDays(i), End: from.AddDays(i)));
        var bars = groups.Select(g =>
        {
            var inGroup = cur.Where(s => s.Date >= g.Start && s.Date <= g.End).ToList();
            var segments = inGroup.GroupBy(s => s.TopicId)
                .Select(x => new TopicMinutes(x.Key, Sum(x)))
                .OrderByDescending(x => x.Minutes)
                .ToList();
            return new StatsBar(g.Start, g.End, Sum(inGroup), segments) { IsCurrent = today >= g.Start && today <= g.End };
        }).ToList();
        var maxBar = Math.Max(60, bars.Count == 0 ? 0 : bars.Max(b => b.TotalMinutes));
        var step = maxBar > 600 ? 300 : maxBar > 300 ? 120 : 60;
        var axisMax = (int)Math.Ceiling(maxBar / (double)step) * step;

        var byTopic = cur.GroupBy(s => s.TopicId)
            .Select(g =>
            {
                var m = Sum(g);
                var c = g.Count();
                return new TopicTime(g.Key, m, total == 0 ? 0 : (int)Math.Round(m * 100.0 / total), c, c == 0 ? 0 : (int)Math.Round((double)m / c));
            })
            .OrderByDescending(t => t.Minutes)
            .ToList();

        // Heatmap of minutes by weekday and start hour inside the study day.
        var hours = Math.Max(1, studyDayEndHour - studyDayStartHour);
        var heat = new int[7, hours];
        foreach (var s in cur)
        {
            var h = s.Start.Hour - studyDayStartHour;
            if (h >= 0 && h < hours) heat[WeekDays.Index(s.Start.DayOfWeek), h] += s.ActualMinutes;
        }
        var heatMax = 0;
        var columns = new int[hours];
        for (var d = 0; d < 7; d++)
            for (var h = 0; h < hours; h++)
            {
                heatMax = Math.Max(heatMax, heat[d, h]);
                columns[h] += heat[d, h];
            }

        PeakWindow? peak = null;
        if (total > 0 && hours >= 2)
        {
            var best = 0;
            for (var h = 1; h < hours - 1; h++)
                if (columns[h] + columns[h + 1] > columns[best] + columns[best + 1]) best = h;
            var inPeak = columns[best] + columns[best + 1];
            if (inPeak > 0) peak = new PeakWindow(studyDayStartHour + best, (int)Math.Round(inPeak * 100.0 / total));
        }

        StrongestDay? strongest = null;
        if (total > 0)
        {
            var daysInRange = Enumerable.Range(0, n).Select(i => from.AddDays(i)).ToList();
            strongest = WeekDays.MondayFirst
                .Select(d =>
                {
                    var count = daysInRange.Count(x => x.DayOfWeek == d);
                    var minutes = Sum(cur.Where(s => s.Start.DayOfWeek == d));
                    return new StrongestDay(d, count == 0 ? 0 : (double)minutes / count);
                })
                .OrderByDescending(x => x.AverageMinutes)
                .First();
        }

        EarlyEnder? early = cur.GroupBy(s => s.TopicId)
            .Select(g => new EarlyEnder(g.Key, g.Count(s => !s.RanAsPlanned), g.Count()))
            .Where(e => e.Sessions >= 3 && e.EndedEarly > 0)
            .OrderByDescending(e => (double)e.EndedEarly / e.Sessions)
            .ThenByDescending(e => e.Sessions)
            .FirstOrDefault();

        var ultra = cur.Where(s => s.Mode == FocusMode.Ultra).ToList();
        var exits = ultra.Where(s => s.Outcome == FocusOutcome.EmergencyExit).OrderByDescending(s => s.Start).Take(3).ToList();

        return new StatsReport
        {
            Range = range,
            From = from,
            To = today,
            FocusMinutes = new Kpi(total, Pct(total, Sum(prev)), DeltaUnit.Percent),
            SessionCount = new Kpi(cur.Count, Pct(cur.Count, prev.Count), DeltaUnit.Percent),
            AverageMinutes = new Kpi(Math.Round(Avg(cur)), Pct(Avg(cur), Avg(prev)), DeltaUnit.Percent),
            CompletedAsPlanned = new Kpi(Math.Round(Rate(cur) * 100), prev.Count == 0 ? null : (int)Math.Round((Rate(cur) - Rate(prev)) * 100), DeltaUnit.Points),
            Bars = bars,
            AxisMaxMinutes = axisMax,
            AxisStepMinutes = step,
            ByTopic = byTopic,
            TotalMinutes = total,
            Heatmap = heat,
            HeatmapStartHour = studyDayStartHour,
            HeatmapMax = heatMax,
            Peak = peak,
            Strongest = strongest,
            EarlyEnder = early,
            Streak = ComputeStreak(all, today),
            Ultra = new UltraSummary(ultra.Count(s => s.RanAsPlanned), ultra.Count, exits),
            Sessions = cur.OrderByDescending(s => s.Start).ToList()
        };
    }

    public static IEnumerable<SessionRecord> Filter(IEnumerable<SessionRecord> sessions, HistoryFilter filter) => filter switch
    {
        HistoryFilter.Focus => sessions.Where(s => s.Mode == FocusMode.Focus),
        HistoryFilter.Ultra => sessions.Where(s => s.Mode == FocusMode.Ultra),
        HistoryFilter.EndedEarly => sessions.Where(s => !s.RanAsPlanned),
        _ => sessions
    };

    /// <summary>
    /// Consecutive days with at least one session. The current streak counts back from today, or from
    /// yesterday when nothing has been logged yet today (the streak is still alive until the day ends).
    /// </summary>
    public static Streak ComputeStreak(IEnumerable<SessionRecord> all, DateOnly today)
    {
        var days = all.Select(s => s.Date).Where(d => d <= today).ToHashSet();
        var current = 0;
        var cursor = days.Contains(today) ? today : today.AddDays(-1);
        while (days.Contains(cursor)) { current++; cursor = cursor.AddDays(-1); }

        int best = 0, run = 0;
        DateOnly? previous = null;
        foreach (var d in days.Order())
        {
            run = previous is { } p && d.DayNumber - p.DayNumber == 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = d;
        }
        return new Streak(current, Math.Max(best, current));
    }

    /// <summary>Minutes focused on each of <paramref name="days"/> consecutive days starting at <paramref name="from"/>.</summary>
    public static int[] DailyMinutes(IEnumerable<SessionRecord> all, DateOnly from, int days)
    {
        var result = new int[days];
        foreach (var s in all)
        {
            var i = s.Date.DayNumber - from.DayNumber;
            if (i >= 0 && i < days) result[i] += s.ActualMinutes;
        }
        return result;
    }
}
