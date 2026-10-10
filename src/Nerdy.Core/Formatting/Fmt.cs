using System.Globalization;

namespace Nerdy.Core.Formatting;

/// <summary>Display formatting shared by every screen, so durations and times read the same everywhere.</summary>
public static class Fmt
{
    /// <summary>Short clock time in the user's culture, e.g. "9:30 AM" or "09:30".</summary>
    public static string Clock(TimeOnly time, CultureInfo? culture = null) =>
        DateTime.Today.Add(time.ToTimeSpan()).ToString("t", culture ?? CultureInfo.CurrentCulture);

    public static string Clock(DateTime time, CultureInfo? culture = null) => Clock(TimeOnly.FromDateTime(time), culture);

    /// <summary>Clock time without AM/PM for compact buttons: "6:45" in 12-hour cultures, "18:45" in 24-hour ones.</summary>
    public static string ClockShort(DateTime time, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        if (!c.DateTimeFormat.ShortTimePattern.Contains('t')) return time.ToString("t", c);
        return $"{(time.Hour % 12 == 0 ? 12 : time.Hour % 12)}:{time.Minute:00}";
    }

    public static string ClockRange(DateTime start, DateTime end, CultureInfo? culture = null) =>
        $"{Clock(start, culture)} – {Clock(end, culture)}";

    /// <summary>Hour axis label: "8 AM" in 12-hour cultures, "8:00" in 24-hour cultures.</summary>
    public static string HourLabel(int hour, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        hour = ((hour % 24) + 24) % 24;
        if (!c.DateTimeFormat.ShortTimePattern.Contains('t'))
            return $"{hour}:00";
        var designator = hour < 12 ? c.DateTimeFormat.AMDesignator : c.DateTimeFormat.PMDesignator;
        return $"{(hour % 12 == 0 ? 12 : hour % 12)} {designator}";
    }

    /// <summary>Compact hour label for dense axes: "8a", "12p" (12-hour cultures) or "8", "12".</summary>
    public static string HourTick(int hour, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        hour = ((hour % 24) + 24) % 24;
        if (!c.DateTimeFormat.ShortTimePattern.Contains('t')) return hour.ToString(c);
        return $"{(hour % 12 == 0 ? 12 : hour % 12)}{(hour < 12 ? "a" : "p")}";
    }

    /// <summary>"25 min", "1h 15m", "1h 2m 5s", "45s".</summary>
    public static string Duration(int totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        int h = totalSeconds / 3600, m = totalSeconds % 3600 / 60, s = totalSeconds % 60;
        if (h == 0 && s == 0) return $"{m} min";
        var parts = new List<string>(3);
        if (h > 0) parts.Add($"{h}h");
        if (m > 0) parts.Add($"{m}m");
        if (s > 0) parts.Add($"{s}s");
        return parts.Count == 0 ? "0s" : string.Join(' ', parts);
    }

    public static string Duration(TimeSpan duration) => Duration((int)Math.Round(duration.TotalSeconds));

    /// <summary>Totals such as "1h 05m" or "45m".</summary>
    public static string HoursMinutes(double minutes)
    {
        var total = (int)Math.Round(Math.Max(0, minutes));
        int h = total / 60, m = total % 60;
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    /// <summary>Countdown text: "18:38" or "1:02:03".</summary>
    public static string Timer(int totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        int h = totalSeconds / 3600, m = totalSeconds % 3600 / 60, s = totalSeconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    /// <summary>"Today", "Tomorrow", "Yesterday", otherwise e.g. "Wed, Oct 21".</summary>
    public static string RelativeDay(DateOnly date, DateOnly today, CultureInfo? culture = null)
    {
        var diff = date.DayNumber - today.DayNumber;
        return diff switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            _ => ShortDay(date, culture)
        };
    }

    /// <summary>"Wed, Oct 21".</summary>
    public static string ShortDay(DateOnly date, CultureInfo? culture = null) =>
        date.ToString("ddd, MMM d", culture ?? CultureInfo.CurrentCulture);

    /// <summary>"Oct 21".</summary>
    public static string MonthDay(DateOnly date, CultureInfo? culture = null) =>
        date.ToString("MMM d", culture ?? CultureInfo.CurrentCulture);

    /// <summary>Due label for reminders: "Today, 11:59 PM", "Tomorrow", "Wed, Oct 21, 3:00 PM".</summary>
    public static string Due(DateTime due, bool hasTime, DateOnly today, CultureInfo? culture = null)
    {
        var day = RelativeDay(DateOnly.FromDateTime(due), today, culture);
        return hasTime ? $"{day}, {Clock(due, culture)}" : day;
    }

    /// <summary>Time-ago label for edited notes: "just now", "10 min ago", "yesterday", "Monday", "Oct 2".</summary>
    public static string Ago(DateTime then, DateTime now, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        var days = DateOnly.FromDateTime(now).DayNumber - DateOnly.FromDateTime(then).DayNumber;
        if (days == 0) return $"{(int)span.TotalHours} h ago";
        if (days == 1) return "yesterday";
        if (days < 7) return then.ToString("dddd", c);
        return then.ToString("MMM d", c);
    }
}
