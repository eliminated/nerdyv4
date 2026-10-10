using System.Globalization;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;

namespace Nerdy.Core.Tests;

public class TimeAndFormattingTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void Weekly_event_expands_on_masked_days_until_the_end_date()
    {
        var first = new DateTime(2026, 10, 5, 9, 0, 0); // Monday
        var mask = WeekDays.ToMask([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);

        var occurrences = Recurrence.Expand(first, first.AddHours(1.5), mask, new DateOnly(2026, 10, 14),
            new DateTime(2026, 10, 1), new DateTime(2026, 11, 1)).ToList();

        Assert.Equal([5, 7, 9, 12, 14], occurrences.Select(o => o.Start.Day));
        Assert.All(occurrences, o => Assert.Equal(TimeSpan.FromHours(1.5), o.End - o.Start));
    }

    [Fact]
    public void One_off_event_is_returned_only_when_it_overlaps_the_range()
    {
        var start = new DateTime(2026, 10, 8, 23, 0, 0);

        Assert.Single(Recurrence.Expand(start, start.AddHours(2), 0, null, new DateTime(2026, 10, 9), new DateTime(2026, 10, 10)));
        Assert.Empty(Recurrence.Expand(start, start.AddHours(2), 0, null, new DateTime(2026, 10, 10), new DateTime(2026, 10, 11)));
    }

    [Fact]
    public void Repeating_event_never_occurs_before_its_first_date()
    {
        var first = new DateTime(2026, 10, 8, 9, 0, 0);

        var occurrences = Recurrence.Expand(first, first.AddHours(1), WeekDays.All, null,
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 10)).ToList();

        Assert.Equal([8, 9], occurrences.Select(o => o.Start.Day));
    }

    [Fact]
    public void Week_starts_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 10, 5), WeekDays.StartOfWeek(new DateOnly(2026, 10, 11)));
        Assert.Equal(64, WeekDays.Bit(DayOfWeek.Sunday));
        Assert.Equal(1, WeekDays.Bit(DayOfWeek.Monday));
    }

    [Theory]
    [InlineData(1500, "25 min")]
    [InlineData(4500, "1h 15m")]
    [InlineData(3725, "1h 2m 5s")]
    [InlineData(45, "45s")]
    [InlineData(0, "0 min")]
    public void Durations(int seconds, string expected) => Assert.Equal(expected, Fmt.Duration(seconds));

    [Theory]
    [InlineData(95, "1h 35m")]
    [InlineData(45, "45m")]
    [InlineData(65, "1h 05m")]
    public void Hours_and_minutes(int minutes, string expected) => Assert.Equal(expected, Fmt.HoursMinutes(minutes));

    [Theory]
    [InlineData(1118, "18:38")]
    [InlineData(3723, "1:02:03")]
    public void Timer_text(int seconds, string expected) => Assert.Equal(expected, Fmt.Timer(seconds));

    [Fact]
    public void Clock_and_hour_labels_follow_culture()
    {
        Assert.Equal("2:00 PM", Fmt.Clock(new TimeOnly(14, 0), En));
        Assert.Equal("14:00", Fmt.Clock(new TimeOnly(14, 0), De));
        Assert.Equal("12 PM", Fmt.HourLabel(12, En));
        Assert.Equal("8 AM", Fmt.HourLabel(8, En));
        Assert.Equal("8:00", Fmt.HourLabel(8, De));
        Assert.Equal("2p", Fmt.HourTick(14, En));
        Assert.Equal("6:45", Fmt.ClockShort(new DateTime(2026, 10, 8, 18, 45, 0), En));
        Assert.Equal("12:05", Fmt.ClockShort(new DateTime(2026, 10, 8, 0, 5, 0), En));
        Assert.Equal("18:45", Fmt.ClockShort(new DateTime(2026, 10, 8, 18, 45, 0), De));
    }

    [Fact]
    public void Relative_days_and_due_labels()
    {
        var today = new DateOnly(2026, 10, 8);

        Assert.Equal("Today", Fmt.RelativeDay(today, today, En));
        Assert.Equal("Tomorrow", Fmt.RelativeDay(today.AddDays(1), today, En));
        Assert.Equal("Wed, Oct 21", Fmt.RelativeDay(new DateOnly(2026, 10, 21), today, En));
        Assert.Equal("Today, 11:59 PM", Fmt.Due(new DateTime(2026, 10, 8, 23, 59, 0), true, today, En));
    }
}
