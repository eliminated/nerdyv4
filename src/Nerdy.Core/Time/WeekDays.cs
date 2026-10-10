namespace Nerdy.Core.Time;

/// <summary>
/// Helpers for the weekly repeat bitmask used by calendar events (Monday = 1 … Sunday = 64)
/// and for Monday-first week arithmetic, which is how Nerdy lays out weeks.
/// </summary>
public static class WeekDays
{
    public const int None = 0;
    public const int All = 127;

    /// <summary>Monday-first order, matching the calendar columns.</summary>
    public static readonly IReadOnlyList<DayOfWeek> MondayFirst =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    /// <summary>0 for Monday … 6 for Sunday.</summary>
    public static int Index(DayOfWeek day) => ((int)day + 6) % 7;

    public static int Bit(DayOfWeek day) => 1 << Index(day);

    public static bool Contains(int mask, DayOfWeek day) => (mask & Bit(day)) != 0;

    public static int ToMask(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (m, d) => m | Bit(d));

    public static IEnumerable<DayOfWeek> FromMask(int mask) => MondayFirst.Where(d => Contains(mask, d));

    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-Index(date.DayOfWeek));

    public static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
