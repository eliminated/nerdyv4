using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Controls;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;
using Windows.UI.Text;

namespace Nerdy.App.ViewModels;

public enum CalendarMode { Day, Week, Month }

/// <summary>A horizontal line or an hour label on a time grid.</summary>
public sealed record LineSlot(double StartHour, string Label) : ITimeSlot
{
    public double EndHour => StartHour;
    public int Lane => 0;
    public int Lanes => 1;
}

public sealed partial class EventSlot(AgendaItem item, Brush background, Brush foreground, FontWeight weight, Action<AgendaItem> open) : ITimeSlot
{
    public AgendaItem Item { get; } = item;
    public string Title { get; } = item.Title;
    public string Detail { get; } = Fmt.ClockRange(item.Start, item.End) + (string.IsNullOrEmpty(item.Subtitle) ? "" : " · " + item.Subtitle);
    public string ToolTip => $"{Title}\n{Detail}";
    public Brush Background { get; } = background;
    public Brush Foreground { get; } = foreground;
    public FontWeight Weight { get; } = weight;
    public double StartHour { get; } = item.StartHour;
    public double EndHour { get; } = Math.Max(item.EndHour, item.StartHour + 0.25);
    public int Lane { get; set; }
    public int Lanes { get; set; } = 1;

    [RelayCommand]
    private void Open() => open(Item);
}

public sealed partial class ChipItem(string title, Brush background, Brush foreground, FontWeight weight, Action open)
{
    public string Title { get; } = title;
    public Brush Background { get; } = background;
    public Brush Foreground { get; } = foreground;
    public FontWeight Weight { get; } = weight;

    [RelayCommand]
    private void Open() => open();
}

public sealed class DayColumnViewModel
{
    public required DateOnly Date { get; init; }
    public required string DayName { get; init; }
    public required string DayNumber { get; init; }
    public required Brush NumberBackground { get; init; }
    public required Brush NumberForeground { get; init; }
    public required Brush ColumnBackground { get; init; }
    public required IReadOnlyList<EventSlot> Events { get; init; }
    public required IReadOnlyList<ChipItem> AllDay { get; init; }
    public required IReadOnlyList<LineSlot> Lines { get; init; }
    /// <summary>Zero or one item: the "now" marker when this column is today.</summary>
    public required IReadOnlyList<LineSlot> Now { get; init; }
}

public sealed partial class MonthCellViewModel(DateOnly date, string number, double opacity, Brush numberBackground, Brush numberForeground,
    FontWeight weight, IReadOnlyList<ChipItem> chips, int more, Action<DateOnly> openDay)
{
    public DateOnly Date { get; } = date;
    public string Number { get; } = number;
    public double Opacity { get; } = opacity;
    public Brush NumberBackground { get; } = numberBackground;
    public Brush NumberForeground { get; } = numberForeground;
    public FontWeight Weight { get; } = weight;
    public IReadOnlyList<ChipItem> Chips { get; } = chips;
    public bool HasMore => more > 0;
    public string MoreText { get; } = $"+{more} more";

    [RelayCommand]
    private void OpenDay() => openDay(Date);
}

public sealed record SummaryRow(string Title, string Time, Brush Dot);

public sealed partial class CalendarViewModel : ObservableObject, INavigationAware
{
    public const double HourHeight = 52;

    private readonly AgendaService _agenda;
    private readonly DataService _data;
    private readonly PreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly DialogService _dialogs;
    private readonly IClock _clock;
    private DateOnly _anchor;

    public CalendarViewModel(AgendaService agenda, DataService data, PreferencesService preferences, ThemeService theme,
        NavigationService navigation, DialogService dialogs, IClock clock)
    {
        _agenda = agenda;
        _data = data;
        _preferences = preferences;
        _theme = theme;
        _navigation = navigation;
        _dialogs = dialogs;
        _clock = clock;
        _anchor = clock.Today;
        for (var h = 0; h < 24; h++)
        {
            HourLabels.Add(new LineSlot(h, h == 0 ? "" : Fmt.HourLabel(h)));
            HourLines.Add(new LineSlot(h, ""));
        }
    }

    public ObservableCollection<ChoiceItem> ViewSegments { get; } = [];
    public ObservableCollection<DayColumnViewModel> Columns { get; } = [];
    public ObservableCollection<MonthCellViewModel> MonthCells { get; } = [];
    public ObservableCollection<MonthCellViewModel> MiniMonth { get; } = [];
    public ObservableCollection<SummaryRow> DaySummary { get; } = [];
    public List<LineSlot> HourLabels { get; } = [];
    public List<LineSlot> HourLines { get; } = [];
    public IReadOnlyList<string> WeekdayHeaders { get; } = WeekDays.MondayFirst.Select(d => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)d]).ToList();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDay), nameof(IsWeek), nameof(IsMonth))]
    public partial CalendarMode Mode { get; set; } = CalendarMode.Week;

    [ObservableProperty] public partial string Title { get; private set; } = "";
    [ObservableProperty] public partial string MiniMonthTitle { get; private set; } = "";
    [ObservableProperty] public partial string DaySummaryText { get; private set; } = "";
    [ObservableProperty] public partial string DayTitle { get; private set; } = "";
    [ObservableProperty] public partial bool HasAllDay { get; private set; }

    public bool IsDay => Mode == CalendarMode.Day;
    public bool IsWeek => Mode == CalendarMode.Week;
    public bool IsMonth => Mode == CalendarMode.Month;
    public double GridHeight => 24 * HourHeight;
    /// <summary>Where the time grid scrolls to initially: half an hour before the study day starts.</summary>
    public double InitialScroll => Math.Max(0, (_preferences.Current.StudyDayStartHour - 0.5) * HourHeight);

    /// <summary>Raised when the visible range changes so the page can reset its scroll position.</summary>
    public event EventHandler? RangeChanged;

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is DateOnly date)
        {
            _anchor = date;
            if (Mode == CalendarMode.Month) Mode = CalendarMode.Week;
        }
        _data.Changed += OnChanged;
        _theme.Changed += OnThemeChanged;
        Refresh();
    }

    public void OnNavigatedFrom()
    {
        _data.Changed -= OnChanged;
        _theme.Changed -= OnThemeChanged;
    }

    [RelayCommand]
    private void Previous() => Move(-1);

    [RelayCommand]
    private void Next() => Move(1);

    [RelayCommand]
    private void GoToday()
    {
        _anchor = _clock.Today;
        Refresh();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NewBlock() => _navigation.Navigate(NavSections.Schedule);

    [RelayCommand]
    private Task NewEventAsync() => EditAsync(null, null);

    /// <summary>Creates an event at a tapped spot in the grid (rounded to the half hour).</summary>
    public Task NewEventAtAsync(DateOnly date, double hour)
    {
        var rounded = Math.Floor(hour * 2) / 2;
        return EditAsync(null, date.ToDateTime(TimeOnly.MinValue).AddHours(rounded));
    }

    private async Task EditAsync(Nerdy.Contracts.CalendarEventDto? existing, DateTime? start)
    {
        var vm = new EventEditorViewModel(_data, _theme, existing, start, _preferences.Current.DefaultReminderMinutes);
        await _dialogs.ShowAsync(new Views.Dialogs.EventEditorDialog(vm));
    }

    private void OpenItem(AgendaItem item)
    {
        if (item.Source == OccurrenceSource.Assignment) _navigation.Navigate(NavSections.Classes);
        else if (_data.Event(item.Id) is { } e) _ = EditAsync(e, null);
    }

    private void Move(int direction)
    {
        _anchor = Mode switch
        {
            CalendarMode.Day => _anchor.AddDays(direction),
            CalendarMode.Week => _anchor.AddDays(7 * direction),
            _ => _anchor.AddMonths(direction)
        };
        Refresh();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetMode(CalendarMode mode)
    {
        Mode = mode;
        Refresh();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.Affects(DataKinds.Events | DataKinds.Topics | DataKinds.Preferences)) Refresh();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        ViewSegments.Clear();
        foreach (var (mode, label) in new[] { (CalendarMode.Day, "Day"), (CalendarMode.Week, "Week"), (CalendarMode.Month, "Month") })
            ViewSegments.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, mode, c => SetMode((CalendarMode)c.Value!), selected: mode == Mode));

        var today = _clock.Today;
        switch (Mode)
        {
            case CalendarMode.Day:
                Title = _anchor.ToString("MMMM yyyy");
                BuildColumns([_anchor], today);
                BuildMonth(MiniMonth, _anchor, today, compact: true);
                MiniMonthTitle = _anchor.ToString("MMMM");
                BuildSummary();
                break;
            case CalendarMode.Week:
                var start = WeekDays.StartOfWeek(_anchor);
                var end = start.AddDays(6);
                Title = start.Month == end.Month ? start.ToString("MMMM yyyy") : $"{start:MMM} – {end:MMM yyyy}";
                BuildColumns(Enumerable.Range(0, 7).Select(start.AddDays).ToList(), today);
                break;
            default:
                Title = _anchor.ToString("MMMM yyyy");
                BuildMonth(MonthCells, _anchor, today, compact: false);
                break;
        }
    }

    private void BuildColumns(IReadOnlyList<DateOnly> days, DateOnly today)
    {
        Columns.Clear();
        var now = _clock.LocalNow;
        foreach (var day in days)
        {
            var items = _agenda.On(day);
            var timed = items.Where(i => !i.IsAllDay).Select(Slot).ToList();
            TimeGridPanel.AssignLanes(timed, s => s.StartHour, s => s.EndHour, (s, lane, lanes) => { s.Lane = lane; s.Lanes = lanes; });
            var isToday = day == today;
            Columns.Add(new DayColumnViewModel
            {
                Date = day,
                DayName = day.ToString("ddd"),
                DayNumber = day.Day.ToString(),
                NumberBackground = _theme.Brush(isToday ? "NerdyAccentBrush" : "NerdyTransparentBrush"),
                NumberForeground = _theme.Brush(isToday ? "NerdyOnAccentBrush" : "NerdyTextPrimaryBrush"),
                ColumnBackground = _theme.Brush(day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "NerdyWeekendColumnBrush" : "NerdyTransparentBrush"),
                Events = timed,
                AllDay = items.Where(i => i.IsAllDay).Select(i => Chip(i)).ToList(),
                Lines = HourLines,
                Now = isToday ? [new LineSlot(now.TimeOfDay.TotalHours, "")] : []
            });
        }
        HasAllDay = Columns.Any(c => c.AllDay.Count > 0);
    }

    private EventSlot Slot(AgendaItem item)
    {
        var color = ColorMix.Parse(item.ColorHex ?? _agenda.TopicColor(item.TopicId));
        return item.IsMilestone
            ? new EventSlot(item, ColorMix.Brush(color), _theme.Brush("NerdyOnAccentBrush"), FontWeights.SemiBold, OpenItem)
            : new EventSlot(item, ColorMix.Brush(_theme.Tint(color, 16)), ColorMix.Brush(_theme.Ink(color)), FontWeights.SemiBold, OpenItem);
    }

    private ChipItem Chip(AgendaItem item)
    {
        var color = ColorMix.Parse(item.ColorHex ?? _agenda.TopicColor(item.TopicId));
        return item.IsMilestone || item.Source == OccurrenceSource.Assignment
            ? new ChipItem(item.Title, ColorMix.Brush(color), _theme.Brush("NerdyOnAccentBrush"), FontWeights.SemiBold, () => OpenItem(item))
            : new ChipItem(item.Title, ColorMix.Brush(_theme.Tint(color, 14)), ColorMix.Brush(_theme.Ink(color)), FontWeights.Medium, () => OpenItem(item));
    }

    private void BuildMonth(ObservableCollection<MonthCellViewModel> target, DateOnly anchor, DateOnly today, bool compact)
    {
        target.Clear();
        var first = new DateOnly(anchor.Year, anchor.Month, 1);
        var start = WeekDays.StartOfWeek(first);
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var inMonth = day.Month == anchor.Month;
            var isToday = day == today;
            var selected = compact && day == _anchor && !isToday;
            var chips = compact ? [] : _agenda.On(day).OrderByDescending(x => x.IsMilestone).ThenBy(x => x.Start).Select(Chip).ToList();
            target.Add(new MonthCellViewModel(day, day.Day.ToString(), inMonth ? 1 : 0.4,
                _theme.Brush(isToday ? "NerdyAccentBrush" : selected ? "NerdyAccentTint14Brush" : "NerdyTransparentBrush"),
                _theme.Brush(isToday ? "NerdyOnAccentBrush" : inMonth ? "NerdyTextPrimaryBrush" : "NerdyTextTertiaryBrush"),
                isToday || selected ? FontWeights.SemiBold : FontWeights.Normal,
                chips.Take(2).ToList(), Math.Max(0, chips.Count - 2), OpenDay));
        }
    }

    private void BuildSummary()
    {
        DayTitle = _anchor.ToString("dddd, MMMM d");
        var items = _agenda.On(_anchor).Where(i => !i.IsAllDay).ToList();
        var hours = items.Sum(i => (i.End - i.Start).TotalMinutes);
        DaySummaryText = items.Count == 0 ? "Nothing planned" : $"{items.Count} {(items.Count == 1 ? "item" : "items")} · {Fmt.HoursMinutes(hours)} planned";
        DaySummary.Clear();
        foreach (var i in items)
            DaySummary.Add(new SummaryRow(i.Title, Fmt.Clock(i.Start), ColorMix.Brush(i.ColorHex ?? _agenda.TopicColor(i.TopicId))));
    }

    private void OpenDay(DateOnly day)
    {
        _anchor = day;
        if (Mode != CalendarMode.Day) Mode = CalendarMode.Day;
        Refresh();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }
}
