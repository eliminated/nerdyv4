using Nerdy.Contracts;
using Nerdy.Core.Stats;

namespace Nerdy.Core.Tests;

public class FocusStatsTests
{
    private static readonly DateOnly Today = new(2026, 10, 8); // Thursday
    private static readonly Guid Calc = Guid.NewGuid();
    private static readonly Guid Bio = Guid.NewGuid();

    private static SessionRecord S(int daysAgo, double hour, int minutes, Guid? subject = null,
        FocusMode mode = FocusMode.Focus, FocusOutcome outcome = FocusOutcome.Completed, int? planned = null, Guid? topic = null) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subject ?? Calc,
        TopicId = topic,
        Start = Today.AddDays(-daysAgo).ToDateTime(TimeOnly.MinValue).AddHours(hour),
        ActualMinutes = minutes,
        PlannedMinutes = planned ?? minutes,
        Mode = mode,
        Outcome = outcome
    };

    [Fact]
    public void Week_kpis_compare_with_the_previous_week()
    {
        SessionRecord[] sessions =
        [
            S(0, 9, 60), S(1, 9, 60), S(2, 14, 30, Bio, outcome: FocusOutcome.EndedEarly, planned: 45),
            S(8, 9, 50), S(9, 9, 50)
        ];

        var r = FocusStats.Build(sessions, StatsRange.Week, Today);

        Assert.Equal(new DateOnly(2026, 10, 2), r.From);
        Assert.Equal(150, r.FocusMinutes.Value);
        Assert.Equal(50, r.FocusMinutes.Delta);            // 150 vs 100
        Assert.Equal(3, r.SessionCount.Value);
        Assert.Equal(50, r.SessionCount.Delta);            // 3 vs 2
        Assert.Equal(67, r.CompletedAsPlanned.Value);
        Assert.Equal(-33, r.CompletedAsPlanned.Delta);     // points
        Assert.Equal(DeltaUnit.Points, r.CompletedAsPlanned.Unit);
    }

    [Fact]
    public void No_previous_data_means_no_delta()
    {
        var r = FocusStats.Build([S(0, 9, 30)], StatsRange.Week, Today);

        Assert.Null(r.FocusMinutes.Delta);
        Assert.Null(r.CompletedAsPlanned.Delta);
    }

    [Theory]
    [InlineData(StatsRange.Week, 7)]
    [InlineData(StatsRange.Month, 30)]
    [InlineData(StatsRange.Term, 8)]
    public void Bars_match_the_range(StatsRange range, int expected)
    {
        var r = FocusStats.Build([S(0, 9, 30)], range, Today);

        Assert.Equal(expected, r.Bars.Count);
        Assert.True(r.Bars[^1].IsCurrent);
        Assert.Equal(30, r.Bars[^1].TotalMinutes);
    }

    [Fact]
    public void Bars_are_split_by_topic_and_axis_rounds_up()
    {
        var r = FocusStats.Build([S(0, 9, 100), S(0, 13, 50, Bio), S(0, 15, 20, Bio)], StatsRange.Week, Today);

        var today = r.Bars[^1];
        Assert.Equal(170, today.TotalMinutes);
        Assert.Equal([Calc, Bio], today.Segments.Select(s => s.SubjectId!.Value));
        Assert.Equal(180, r.AxisMaxMinutes);
        Assert.Equal(60, r.AxisStepMinutes);
        Assert.Equal(59, r.BySubject[0].Percent);
    }

    [Fact]
    public void Heatmap_and_insights()
    {
        SessionRecord[] sessions =
        [
            S(0, 9.25, 60), S(1, 10, 45), S(7 - 1, 9, 30, Bio, outcome: FocusOutcome.EndedEarly),
            S(2, 16, 20, Bio, outcome: FocusOutcome.EndedEarly), S(3, 16, 20, Bio)
        ];

        var r = FocusStats.Build(sessions, StatsRange.Week, Today);

        Assert.Equal(60, r.Heatmap[3, 1]); // Thursday, 9 AM column
        Assert.Equal(9, r.Peak!.StartHour);
        Assert.Equal(DayOfWeek.Thursday, r.Strongest!.Day);
        Assert.Equal(Bio, r.EarlyEnder!.SubjectId);
        Assert.Equal(2, r.EarlyEnder.EndedEarly);
    }

    [Fact]
    public void Early_ender_needs_at_least_three_sessions()
    {
        var r = FocusStats.Build([S(0, 9, 10, outcome: FocusOutcome.EndedEarly), S(1, 9, 10)], StatsRange.Week, Today);

        Assert.Null(r.EarlyEnder);
    }

    [Fact]
    public void Streak_survives_until_today_ends()
    {
        // Yesterday and the two days before, nothing yet today; an older 5-day run.
        SessionRecord[] sessions = [S(1, 9, 10), S(2, 9, 10), S(3, 9, 10), .. Enumerable.Range(10, 5).Select(d => S(d, 9, 10))];

        var streak = FocusStats.ComputeStreak(sessions, Today);

        Assert.Equal(3, streak.Current);
        Assert.Equal(5, streak.Best);
    }

    [Fact]
    public void Ultra_summary_counts_held_sessions_and_recent_exits()
    {
        SessionRecord[] sessions =
        [
            S(0, 9, 50, mode: FocusMode.Ultra),
            S(1, 9, 20, mode: FocusMode.Ultra, outcome: FocusOutcome.EmergencyExit, planned: 60) with { ExitReason = ExitReason.Family, ExitNote = "Pick up my brother" },
            S(2, 9, 25)
        ];

        var r = FocusStats.Build(sessions, StatsRange.Week, Today);

        Assert.Equal(1, r.Ultra.Held);
        Assert.Equal(2, r.Ultra.Total);
        Assert.Equal(ExitReason.Family, r.Ultra.RecentExits.Single().ExitReason);
        Assert.Equal(2, FocusStats.Filter(r.Sessions, HistoryFilter.Ultra).Count());
        Assert.Single(FocusStats.Filter(r.Sessions, HistoryFilter.EndedEarly));
    }

    [Fact]
    public void Daily_minutes_buckets_by_day()
    {
        var days = FocusStats.DailyMinutes([S(0, 9, 30), S(0, 13, 15), S(3, 9, 40)], Today.AddDays(-3), 4);

        Assert.Equal([40, 0, 0, 45], days);
    }

    [Fact]
    public void Subject_time_splits_by_topic_with_shares_of_the_subject()
    {
        var limits = Guid.NewGuid();
        var series = Guid.NewGuid();
        SessionRecord[] sessions =
        [
            S(0, 9, 60, topic: limits), S(1, 9, 30, topic: limits), S(1, 14, 30, topic: series), S(2, 9, 30),
            S(0, 16, 50, Bio)
        ];

        var calc = FocusStats.Build(sessions, StatsRange.Week, Today).BySubject.Single(s => s.SubjectId == Calc);

        Assert.Equal(150, calc.Minutes);
        Assert.Equal(4, calc.Sessions);
        Assert.Equal([limits, series, null], calc.Topics.Select(t => t.TopicId));
        Assert.Equal([90, 30, 30], calc.Topics.Select(t => t.Minutes));
        Assert.Equal([60, 20, 20], calc.Topics.Select(t => t.Percent));
    }
}
