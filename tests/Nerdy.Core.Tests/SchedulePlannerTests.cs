using Nerdy.Core.Scheduling;

namespace Nerdy.Core.Tests;

public class SchedulePlannerTests
{
    // The prototype's "now": Thursday, Oct 8 2026, 10:20 AM, in the week of Mon Oct 5.
    private static readonly DateTime Now = new(2026, 10, 8, 10, 20, 0);

    // The prototype's weekly timetable (Mon = Oct 5 … Sun = Oct 11).
    private static readonly Dictionary<DayOfWeek, (double s, double e, string title)[]> Week = new()
    {
        [DayOfWeek.Monday] = [(9, 10.5, "Calculus II lecture"), (13, 14, "Biology lab"), (16, 17.5, "Literature review")],
        [DayOfWeek.Tuesday] = [(10, 11.5, "History seminar"), (14, 15, "Problem set 4")],
        [DayOfWeek.Wednesday] = [(9, 10.5, "Calculus II lecture"), (12, 13, "Office hours"), (15, 16.5, "Genetics reading")],
        [DayOfWeek.Thursday] = [(9, 10, "Cell structure review"), (11, 12.5, "Methods draft"), (14, 15, "Series & sequences"), (16.5, 17.5, "History reading")],
        [DayOfWeek.Friday] = [(9, 10.5, "Calculus II lecture"), (13, 15, "Data analysis")],
        [DayOfWeek.Saturday] = [(10, 11.5, "Bio flashcards")],
        [DayOfWeek.Sunday] = [(17, 18, "Weekly review")],
    };

    private static IEnumerable<BusySlot> Timetable(DateOnly d) =>
        Week[d.DayOfWeek].Select(x => new BusySlot(d.ToDateTime(TimeOnly.MinValue).AddHours(x.s), d.ToDateTime(TimeOnly.MinValue).AddHours(x.e), x.title));

    private static StudyBlockRequest MethodsBlock(ScheduleScope scope = ScheduleScope.Week, params DayOfWeek[] days) => new()
    {
        Title = "Methods writing block",
        Scope = scope,
        Days = days.Length == 0 ? [DayOfWeek.Thursday, DayOfWeek.Friday] : days,
        Start = new TimeOnly(14, 0),
        Duration = TimeSpan.FromHours(1.5)
    };

    [Fact]
    public void Prototype_scenario_reports_both_clashes_with_nearby_suggestions()
    {
        var planner = new SchedulePlanner(Timetable);

        var plan = planner.Plan(MethodsBlock(), null, Now);

        Assert.Equal(2, plan.Count);
        var thu = plan[0];
        Assert.Equal(new DateOnly(2026, 10, 8), thu.Date);
        Assert.Equal(PlanStatus.Conflict, thu.Status);
        Assert.Equal(ConflictKind.Overlap, thu.Conflict);
        Assert.Equal("Series & sequences", thu.ConflictWith?.Title);
        Assert.Equal([new TimeOnly(15, 0), new TimeOnly(12, 30)], thu.Suggestions.Select(s => s.Start));

        var fri = plan[1];
        Assert.Equal("Data analysis", fri.ConflictWith?.Title);
        Assert.Equal([new TimeOnly(15, 0), new TimeOnly(15, 30)], fri.Suggestions.Select(s => s.Start));
    }

    [Fact]
    public void Past_days_of_the_week_are_not_targeted()
    {
        var planner = new SchedulePlanner(Timetable);

        var dates = planner.TargetDates(MethodsBlock(ScheduleScope.Week, DayOfWeek.Monday, DayOfWeek.Saturday), Now);

        Assert.Equal([new DateOnly(2026, 10, 10)], dates);
    }

    [Fact]
    public void Once_targets_only_the_first_chosen_day()
    {
        var planner = new SchedulePlanner(Timetable);

        var dates = planner.TargetDates(MethodsBlock(ScheduleScope.Once, DayOfWeek.Saturday, DayOfWeek.Friday), Now);

        Assert.Equal([new DateOnly(2026, 10, 9)], dates);
    }

    [Fact]
    public void Month_repeats_on_weekdays_through_month_end()
    {
        var planner = new SchedulePlanner(Timetable);

        var dates = planner.TargetDates(MethodsBlock(ScheduleScope.Month, DayOfWeek.Tuesday), Now);

        Assert.Equal([13, 20, 27], dates.Select(d => d.Day));
    }

    [Fact]
    public void Free_slot_is_ok_and_blocks_later_sessions_moved_onto_the_same_day()
    {
        var planner = new SchedulePlanner(Timetable);
        var request = MethodsBlock(ScheduleScope.Week, DayOfWeek.Saturday, DayOfWeek.Sunday) with { Start = new TimeOnly(12, 0) };
        var overrides = new Dictionary<DateOnly, SessionOverride>
        {
            // Move Sunday's session onto Saturday at the same time as Saturday's own session.
            [new DateOnly(2026, 10, 11)] = new SessionOverride.MoveTo(new DateOnly(2026, 10, 10), new TimeOnly(12, 0))
        };

        var plan = planner.Plan(request, overrides, Now);

        Assert.Equal(PlanStatus.Ok, plan[0].Status);
        Assert.Equal(PlanStatus.Conflict, plan[1].Status);
        Assert.Equal("Methods writing block", plan[1].ConflictWith?.Title);
    }

    [Fact]
    public void Overrides_move_and_skip_sessions()
    {
        var planner = new SchedulePlanner(Timetable);
        var first = planner.Plan(MethodsBlock(), null, Now);

        var resolved = SchedulePlanner.WithFirstSuggestions(first, null);
        var plan = planner.Plan(MethodsBlock(), resolved, Now);
        Assert.All(plan, s => Assert.Equal(PlanStatus.Moved, s.Status));
        Assert.Equal(new TimeOnly(15, 0), plan[0].Start);

        var skipped = planner.Plan(MethodsBlock(), SchedulePlanner.WithConflictsSkipped(first, null), Now);
        Assert.All(skipped, s => Assert.Equal(PlanStatus.Skipped, s.Status));
    }

    [Fact]
    public void Time_already_passed_today_is_a_conflict()
    {
        var planner = new SchedulePlanner(_ => []);
        var request = MethodsBlock(ScheduleScope.Once, DayOfWeek.Thursday) with { Start = new TimeOnly(9, 0) };

        var plan = planner.Plan(request, null, Now);

        Assert.Equal(ConflictKind.InPast, plan.Single().Conflict);
        Assert.Equal(new TimeOnly(10, 30), plan.Single().Suggestions[0].Start);
    }

    [Fact]
    public void Session_running_past_the_study_day_is_a_conflict()
    {
        var planner = new SchedulePlanner(_ => []);
        var request = MethodsBlock(ScheduleScope.Once, DayOfWeek.Friday) with { Start = new TimeOnly(19, 0) };

        var plan = planner.Plan(request, null, Now);

        Assert.Equal(ConflictKind.OutsideStudyDay, plan.Single().Conflict);
        Assert.Equal(new TimeOnly(18, 30), plan.Single().Suggestions[0].Start);
    }

    [Fact]
    public void Full_day_suggests_other_days_then_next_week()
    {
        var fullFriday = new DateOnly(2026, 10, 9);
        var allBusy = new SchedulePlanner(d => d <= fullFriday.AddDays(6)
            ? [new BusySlot(d.ToDateTime(new TimeOnly(8, 0)), d.ToDateTime(new TimeOnly(20, 0)), "Field trip")]
            : []);
        var oneBusy = new SchedulePlanner(d => d == fullFriday
            ? [new BusySlot(d.ToDateTime(new TimeOnly(8, 0)), d.ToDateTime(new TimeOnly(20, 0)), "Field trip")]
            : []);
        var request = MethodsBlock(ScheduleScope.Once, DayOfWeek.Friday);

        var dayFull = oneBusy.Plan(request, null, Now).Single();
        Assert.True(dayFull.DayFull);
        Assert.False(dayFull.WeekFull);
        Assert.Equal([new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11)], dayFull.Suggestions.Select(s => s.Date));

        var weekFull = allBusy.Plan(request, null, Now).Single();
        Assert.True(weekFull.WeekFull);
        Assert.Equal(new DateOnly(2026, 10, 16), weekFull.Suggestions.Single().Date);
    }
}
