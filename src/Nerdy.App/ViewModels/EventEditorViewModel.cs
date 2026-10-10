using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentIcons.Common;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;

namespace Nerdy.App.ViewModels;

public sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>
/// Create or edit one calendar event (a single occurrence or a weekly series). Start and end are picked with quick
/// buttons like the Focus durations: the start from Now / In 15 min / Next hour, the end as a clock time 15, 25, 45
/// or 60 minutes after the start; Custom on either shows a time picker.
/// </summary>
public sealed partial class EventEditorViewModel : ObservableObject
{
    private enum StartPick { Now, In15, NextHour, Custom }

    private static readonly int[] Lengths = [15, 25, 45, 60];

    /// <summary>The latest end time the time picker offers (5-minute steps) on the event's own day.</summary>
    private static readonly TimeSpan LastEnd = new(23, 55, 0);

    private readonly DataService _data;
    private readonly ThemeService _theme;
    private readonly CalendarEventDto? _original;
    private readonly bool _ready;
    private bool _startNow;     // "Now" takes the start when saving, so an auto-focus block starts right away
    private bool _startCustom;
    private bool _endCustom;
    private bool _settingStart; // a quick pick is setting Date and StartTime itself

    public static readonly IReadOnlyList<Option<int?>> ReminderOptions =
    [
        new("No reminder", null), new("At start time", 0), new("5 minutes before", 5), new("10 minutes before", 10),
        new("15 minutes before", 15), new("30 minutes before", 30), new("1 hour before", 60), new("1 day before", 1440)
    ];

    public static readonly IReadOnlyList<Option<FocusMode>> FocusModes = [new("Focus", FocusMode.Focus), new("Ultra-Focus", FocusMode.Ultra)];

    public EventEditorViewModel(DataService data, ThemeService theme, CalendarEventDto? existing, DateTime? start, int defaultReminder)
    {
        _data = data;
        _theme = theme;
        _original = existing;

        Topics = [new Option<Guid?>("No topic", null), .. data.Topics.Select(t => new Option<Guid?>(t.Name, t.Id))];
        var fresh = existing is null && start is null; // "New event" button rather than a calendar slot
        var s = existing?.StartsAt.LocalDateTime ?? start ?? NextHour(DateTime.Now);
        var e = existing?.EndsAt.LocalDateTime ?? Min(s.AddHours(1), s.Date + LastEnd); // events don't cross midnight

        Title = existing?.Title ?? "";
        Subtitle = existing?.Subtitle ?? "";
        Topic = Topics.FirstOrDefault(t => t.Value == existing?.TopicId) ?? Topics[0];
        Date = new DateTimeOffset(s.Date);
        StartTime = s.TimeOfDay;
        EndTime = e.TimeOfDay;
        IsAllDay = existing?.IsAllDay ?? false;
        IsMilestone = existing?.IsMilestone ?? false;
        Repeats = (existing?.RepeatDays ?? 0) != 0;
        RepeatUntil = existing?.RepeatUntil is { } u ? new DateTimeOffset(u.ToDateTime(TimeOnly.MinValue)) : null;
        Reminder = ReminderOptions.FirstOrDefault(r => r.Value == (existing is null ? defaultReminder : existing.ReminderMinutes)) ?? ReminderOptions[0];
        AutoFocus = existing?.AutoFocus ?? false;
        AutoFocusMode = FocusModes.First(m => m.Value == (existing?.AutoFocusMode ?? FocusMode.Focus));

        var mask = existing?.RepeatDays is > 0 ? existing.RepeatDays : WeekDays.Bit(s.DayOfWeek);
        foreach (var d in WeekDays.MondayFirst)
            DayChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, d.ToString()[..2], d, ToggleDay, selected: WeekDays.Contains(mask, d)));

        // An existing event or a clicked slot shows its time with no quick pick selected; Custom changes it.
        StartChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "Now", StartPick.Now, PickStart));
        StartChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "In 15 min", StartPick.In15, PickStart));
        StartChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "Next hour", StartPick.NextHour, PickStart, selected: fresh));
        StartChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "Custom", StartPick.Custom, PickStart, Symbol.Timer));

        _ready = true;
        UpdateTimes();
    }

    private static DateTime NextHour(DateTime now) => now.Date.AddHours(now.Hour + 1);

    private static DateTime RoundUpToFive(DateTime t)
    {
        var minutes = (t.Hour * 60 + t.Minute + (t.Second > 0 ? 1 : 0) + 4) / 5 * 5;
        return t.Date.AddMinutes(minutes);
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    public bool IsNew => _original is null;
    public string DialogTitle => IsNew ? "New event" : "Edit event";
    public bool IsSeries => _original?.RepeatDays is > 0;

    public IReadOnlyList<Option<Guid?>> Topics { get; }
    public ObservableCollection<ChoiceItem> DayChips { get; } = [];
    public ObservableCollection<ChoiceItem> StartChips { get; } = [];
    public ObservableCollection<ChoiceItem> EndChips { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial string Title { get; set; }
    [ObservableProperty] public partial string Subtitle { get; set; }
    [ObservableProperty] public partial Option<Guid?> Topic { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StartsInPast))] public partial DateTimeOffset? Date { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StartsInPast))] public partial TimeSpan StartTime { get; set; }
    [ObservableProperty] public partial TimeSpan EndTime { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTimes), nameof(StartsInPast), nameof(ShowAutoFocusHint))] public partial bool IsAllDay { get; set; }
    [ObservableProperty] public partial bool IsMilestone { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StartsInPast))] public partial bool Repeats { get; set; }
    [ObservableProperty] public partial DateTimeOffset? RepeatUntil { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StartsInPast))] public partial Option<int?> Reminder { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StartsInPast), nameof(ShowAutoFocusHint))] public partial bool AutoFocus { get; set; }
    [ObservableProperty] public partial Option<FocusMode> AutoFocusMode { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty] public partial string StartText { get; private set; } = "";
    [ObservableProperty] public partial string EndText { get; private set; } = "";
    [ObservableProperty] public partial string LengthText { get; private set; } = "";
    [ObservableProperty] public partial bool IsStartCustom { get; private set; }
    [ObservableProperty] public partial bool IsEndCustom { get; private set; }

    public bool ShowTimes => !IsAllDay;
    public bool ShowAutoFocusHint => AutoFocus && !IsAllDay;
    public bool CanSave => !string.IsNullOrWhiteSpace(Title);

    /// <summary>A one-off event that has already started can't be reminded about or prompt a focus session; say so.</summary>
    public bool StartsInPast => !IsAllDay && !Repeats && !_startNow && (Reminder?.Value is not null || AutoFocus)
        && Date is { } date && date.Date + StartTime <= DateTime.Now;

    // Moving the start keeps the event's length (as other calendars do), so the end doesn't end up before the start.
    partial void OnStartTimeChanged(TimeSpan oldValue, TimeSpan newValue)
    {
        if (!_ready) return;
        var length = EndTime - oldValue;
        if (length <= TimeSpan.Zero) length = TimeSpan.FromHours(1);
        var end = newValue + length;
        EndTime = end > LastEnd ? LastEnd : end;
        UpdateTimes();
    }

    partial void OnEndTimeChanged(TimeSpan value) => UpdateTimes();

    partial void OnDateChanged(DateTimeOffset? value)
    {
        if (!_ready || _settingStart) return;
        // A different day means "Now" / "In 15 min" / "Next hour" no longer describe the start.
        if (!_startCustom) SelectStart(null);
        UpdateTimes();
    }

    private void PickStart(ChoiceItem item)
    {
        var pick = (StartPick)item.Value!;
        SelectStart(item);
        _startCustom = pick == StartPick.Custom;
        _startNow = pick == StartPick.Now;
        if (pick != StartPick.Custom)
        {
            var now = DateTime.Now;
            var at = pick switch
            {
                StartPick.Now => now.Date.AddHours(now.Hour).AddMinutes(now.Minute),
                StartPick.In15 => RoundUpToFive(now.AddMinutes(15)),
                _ => NextHour(now)
            };
            _settingStart = true;
            Date = new DateTimeOffset(at.Date);
            StartTime = at.TimeOfDay;
            _settingStart = false;
        }
        UpdateTimes();
    }

    private void SelectStart(ChoiceItem? item)
    {
        foreach (var c in StartChips) c.IsSelected = c == item;
        if (item is null) _startNow = false;
    }

    private void PickEnd(ChoiceItem item)
    {
        _endCustom = item.Value is null;
        if (item.Value is int minutes) EndTime = StartTime + TimeSpan.FromMinutes(minutes);
        UpdateTimes();
    }

    /// <summary>Refreshes the start/end texts and the end buttons, whose times follow the start.</summary>
    private void UpdateTimes()
    {
        if (!_ready) return;
        var day = Date?.Date ?? DateTime.Today;
        var length = (int)Math.Round((EndTime - StartTime).TotalMinutes);
        StartText = Fmt.Clock(day + StartTime);
        EndText = Fmt.Clock(day + EndTime);
        LengthText = length > 0 ? Fmt.Duration(length * 60) : "";
        IsStartCustom = _startCustom;
        IsEndCustom = _endCustom || !Lengths.Contains(length);
        OnPropertyChanged(nameof(StartsInPast));

        EndChips.Clear();
        foreach (var m in Lengths)
            EndChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, Fmt.ClockShort(day + StartTime + TimeSpan.FromMinutes(m)), m, PickEnd,
                selected: !IsEndCustom && length == m)
            {
                ToolTip = Fmt.Duration(m * 60),
                IsEnabled = StartTime + TimeSpan.FromMinutes(m) <= LastEnd // events don't cross midnight
            });
        EndChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, "Custom", null, PickEnd, Symbol.Timer, selected: IsEndCustom));
    }

    /// <summary>Validates and saves. Returns false (with <see cref="Error"/> set) when something needs fixing.</summary>
    public bool Save()
    {
        if (Date is not { } date) { Error = "Pick a date."; return false; }
        var day = date.Date;
        var start = IsAllDay ? day : day + StartTime;
        var end = IsAllDay ? day.AddDays(1) : day + EndTime;
        if (_startNow && !IsAllDay)
        {
            // "Now" means when it's saved, keeping the chosen length.
            var now = DateTime.Now;
            var length = end - start;
            start = now.Date.AddHours(now.Hour).AddMinutes(now.Minute);
            end = Min(start + length, start.Date + LastEnd);
        }
        if (end <= start) { Error = "The event has to end after it starts."; return false; }

        var mask = Repeats ? WeekDays.ToMask(DayChips.Where(c => c.IsSelected).Select(c => (DayOfWeek)c.Value!)) : 0;
        if (Repeats && mask == 0) { Error = "Pick at least one day to repeat on."; return false; }
        // A repeating series starts on its first matching day on or after the chosen date.
        if (mask != 0)
            while (!WeekDays.Contains(mask, start.DayOfWeek)) { start = start.AddDays(1); end = end.AddDays(1); }

        var dto = (_original ?? new CalendarEventDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow }) with
        {
            Title = Title.Trim(),
            Subtitle = string.IsNullOrWhiteSpace(Subtitle) ? null : Subtitle.Trim(),
            TopicId = Topic.Value,
            StartsAt = new DateTimeOffset(start),
            EndsAt = new DateTimeOffset(end),
            IsAllDay = IsAllDay,
            IsMilestone = IsMilestone,
            RepeatDays = mask,
            RepeatUntil = mask != 0 && RepeatUntil is { } until ? DateOnly.FromDateTime(until.Date) : null,
            ReminderMinutes = Reminder.Value,
            AutoFocus = AutoFocus && !IsAllDay,
            AutoFocusMode = AutoFocusMode.Value
        };
        _data.Save(dto);
        return true;
    }

    public void Delete()
    {
        if (_original is not null) _data.Delete(_original);
    }

    public CalendarEventDto? Original => _original;

    private void ToggleDay(ChoiceItem item) => item.IsSelected = !item.IsSelected;
}
