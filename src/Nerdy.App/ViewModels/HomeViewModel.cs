using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Classes;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.App.ViewModels.Items;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Stats;
using Nerdy.Core.Time;

namespace Nerdy.App.ViewModels;

public sealed record ScheduleRow(string StartText, string Title, string Detail, Brush Dot, double Opacity, bool IsPast, bool ShowNowBefore, string NowText);
public sealed record WeekBar(string Label, double Height, Brush Fill, string ToolTip);
public sealed record StreakCell(Brush Fill, string ToolTip);
public sealed record HourLine(string Label, double Top);

public sealed partial class TopicCardViewModel(Guid id, string name, Brush color, int percent, string next, Action<Guid> open)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public Brush Color { get; } = color;
    public double Percent { get; } = percent;
    public string PercentText { get; } = $"{percent}%";
    public string NextText { get; } = $"Next · {next}";

    [RelayCommand]
    private void Open() => open(Id);
}

public sealed partial class AgendaBlock(AgendaItem item, double top, double height, Brush background, Brush foreground, double opacity, bool isNext, Action<AgendaItem> focus)
{
    public string Title { get; } = item.Title;
    public string Detail { get; } = $"{Fmt.ClockRange(item.Start, item.End)}{(string.IsNullOrEmpty(item.Subtitle) ? "" : " · " + item.Subtitle)}";
    public double Top { get; } = top;
    public double Height { get; } = height;
    public Brush Background { get; } = background;
    public Brush Foreground { get; } = foreground;
    public double Opacity { get; } = opacity;
    public bool IsNext { get; } = isNext;

    [RelayCommand]
    private void Focus() => focus(item);
}

public sealed partial class HomeViewModel : ObservableObject, INavigationAware
{
    private const double AgendaHourHeight = 56;
    private const int AgendaStartHour = 8;
    private const int AgendaEndHour = 18;

    private readonly AgendaService _agenda;
    private readonly DataService _data;
    private readonly ClassesService _classes;
    private readonly PreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly AuthService _auth;
    private readonly NavigationService _navigation;
    private readonly FocusSessionService _focus;
    private readonly ToastService _toasts;
    private readonly IClock _clock;
    private DispatcherQueueTimer? _minuteTimer;
    private AgendaItem? _next;

    public HomeViewModel(AgendaService agenda, DataService data, ClassesService classes, PreferencesService preferences, ThemeService theme,
        AuthService auth, NavigationService navigation, FocusSessionService focus, ToastService toasts, IClock clock)
    {
        _agenda = agenda;
        _data = data;
        _classes = classes;
        _preferences = preferences;
        _theme = theme;
        _auth = auth;
        _navigation = navigation;
        _focus = focus;
        _toasts = toasts;
        _clock = clock;
    }

    public ObservableCollection<ScheduleRow> TodayRows { get; } = [];
    public ObservableCollection<WeekBar> WeekBars { get; } = [];
    public ObservableCollection<StreakCell> StreakCells { get; } = [];
    public ObservableCollection<ReminderItemViewModel> Reminders { get; } = [];
    public ObservableCollection<ReminderItemViewModel> Assignments { get; } = [];
    public ObservableCollection<TopicCardViewModel> Topics { get; } = [];
    public ObservableCollection<HourLine> Hours { get; } = [];
    public ObservableCollection<AgendaBlock> AgendaBlocks { get; } = [];
    public ObservableCollection<ChoiceItem> ModeSegments { get; } = [];
    public ObservableCollection<ChoiceItem> DurationChips { get; } = [];

    // ----- Header -----
    [ObservableProperty] public partial string Greeting { get; private set; } = "";
    [ObservableProperty] public partial string DateText { get; private set; } = "";
    [ObservableProperty] public partial string AgendaSubtitle { get; private set; } = "";

    // ----- Layout & widgets -----
    [ObservableProperty] public partial bool IsDashboard { get; private set; } = true;
    [ObservableProperty] public partial bool ShowNext { get; private set; } = true;
    [ObservableProperty] public partial bool ShowStats { get; private set; } = true;
    [ObservableProperty] public partial bool ShowStreak { get; private set; } = true;
    [ObservableProperty] public partial bool ShowSchedule { get; private set; } = true;
    [ObservableProperty] public partial bool ShowReminders { get; private set; } = true;
    [ObservableProperty] public partial bool ShowTopics { get; private set; } = true;
    [ObservableProperty] public partial bool ShowAssignments { get; private set; }
    public bool IsAgenda => !IsDashboard;

    // ----- Next up -----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NextActionLabel))] public partial bool HasNext { get; private set; }
    public string NextActionLabel => HasNext ? "Focus on this" : "Start focus";
    [ObservableProperty] public partial string NextBadge { get; private set; } = "";
    [ObservableProperty] public partial string NextTitle { get; private set; } = "";
    [ObservableProperty] public partial string NextDetail { get; private set; } = "";

    // ----- Stats widgets -----
    [ObservableProperty] public partial string TodayFocusText { get; private set; } = "0m";
    [ObservableProperty] public partial string StreakText { get; private set; } = "0 days";

    // ----- Empty states -----
    [ObservableProperty] public partial bool HasTodayRows { get; private set; }
    [ObservableProperty] public partial bool HasReminders { get; private set; }
    [ObservableProperty] public partial bool HasTopics { get; private set; }

    // ----- Agenda layout -----
    public double AgendaHeight => (AgendaEndHour - AgendaStartHour) * AgendaHourHeight + 20;
    [ObservableProperty] public partial double NowTop { get; private set; }
    [ObservableProperty] public partial bool ShowNowLine { get; private set; }

    // ----- Quick focus -----
    [ObservableProperty] public partial string DurationText { get; private set; } = "25";
    [ObservableProperty] public partial string StartLabel { get; private set; } = "";
    [ObservableProperty] public partial Symbol StartIcon { get; private set; } = Symbol.Play;

    public void OnNavigatedTo(object? parameter)
    {
        _data.Changed += OnDataChanged;
        _classes.Changed += OnRefresh;
        _theme.Changed += OnRefresh;
        _preferences.Changed += OnPreferencesChanged;
        _focus.PropertyChanged += OnFocusDraftChanged;
        _minuteTimer ??= CreateMinuteTimer();
        _minuteTimer.Start();
        Refresh();
    }

    public void OnNavigatedFrom()
    {
        _data.Changed -= OnDataChanged;
        _classes.Changed -= OnRefresh;
        _theme.Changed -= OnRefresh;
        _preferences.Changed -= OnPreferencesChanged;
        _focus.PropertyChanged -= OnFocusDraftChanged;
        _minuteTimer?.Stop();
    }

    [RelayCommand] private void OpenCalendar() => _navigation.Navigate(NavSections.Calendar);
    [RelayCommand] private void OpenReminders() => _navigation.Navigate(NavSections.Reminders);
    [RelayCommand] private void OpenStats() => _navigation.Navigate(NavSections.Stats);
    [RelayCommand] private void OpenSchedule() => _navigation.Navigate(NavSections.Schedule);
    [RelayCommand] private void OpenFocus() => _navigation.Navigate(NavSections.Focus);
    [RelayCommand] private void OpenClasses() => _navigation.Navigate(NavSections.Classes);
    [RelayCommand] private void Customize() => _navigation.Navigate(NavSections.Settings, "home");

    [RelayCommand]
    private void FocusOnNext()
    {
        if (_next is not null) FocusOn(_next);
        else _navigation.Navigate(NavSections.Focus);
    }

    [RelayCommand]
    private void OpenNextNotes() => _navigation.Navigate(NavSections.Notes, new NotesTarget(_next?.TopicId, null, CreateNew: false));

    [RelayCommand]
    private async Task StartFocusAsync() => await _focus.StartAsync();

    private void FocusOn(AgendaItem item)
    {
        _focus.DraftTask = item.Title;
        _focus.DraftTopicId = item.TopicId;
        _navigation.Navigate(NavSections.Focus);
    }

    private void OnDataChanged(object? sender, DataChangedEventArgs e) => Refresh();
    private void OnRefresh(object? sender, EventArgs e) => Refresh();
    private void OnPreferencesChanged(object? sender, Preferences e) => Refresh();

    private void OnFocusDraftChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSessionService.DraftMode) or nameof(FocusSessionService.DraftSeconds)) RefreshQuickFocus();
    }

    private DispatcherQueueTimer CreateMinuteTimer()
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += (_, _) => Refresh();
        return timer;
    }

    private void Refresh()
    {
        var prefs = _preferences.Current;
        var now = _clock.LocalNow;
        var today = _clock.Today;

        IsDashboard = prefs.HomeLayout == HomeLayout.Dashboard;
        OnPropertyChanged(nameof(IsAgenda));
        bool W(string id) => !prefs.Widgets.TryGetValue(id, out var on) || on;
        ShowNext = W("next");
        ShowStats = W("stats");
        ShowStreak = W("streak");
        ShowSchedule = W("schedule");
        ShowReminders = W("reminders");
        ShowTopics = W("topics");
        ShowAssignments = W("assignments") && _classes.MyAssignments.Any(a => !a.IsDone);

        var first = (_auth.User?.DisplayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        Greeting = now.Hour switch { < 12 => $"Good morning, {first}", < 18 => $"Good afternoon, {first}", _ => $"Good evening, {first}" };
        DateText = now.ToString("dddd, MMMM d");

        var todayItems = _agenda.On(today).Where(i => !i.IsAllDay).ToList();
        AgendaSubtitle = $"{DateText} · {todayItems.Count} {(todayItems.Count == 1 ? "session" : "sessions")} planned";
        BuildNext(todayItems, now);
        BuildToday(todayItems, now);
        BuildFocusWidgets(today);
        BuildReminders(today);
        BuildTopics();
        BuildAgenda(todayItems, now);
        RefreshQuickFocus();
    }

    private void BuildNext(List<AgendaItem> today, DateTime now)
    {
        _next = today.FirstOrDefault(i => i.End > now);
        HasNext = _next is not null;
        if (_next is null)
        {
            NextBadge = "All clear";
            NextTitle = "Nothing else scheduled today";
            NextDetail = "Plan a study block or start a focus session.";
            return;
        }
        var minutes = (int)Math.Ceiling((_next.Start - now).TotalMinutes);
        NextBadge = _next.Start <= now ? "Happening now" : minutes < 60 ? $"Next up · in {minutes} min" : $"Next up · {Fmt.Clock(_next.Start)}";
        NextTitle = _next.Title;
        var topic = _next.ColorHex is null ? _agenda.TopicName(_next.TopicId) : null;
        NextDetail = string.Join(" · ", new[] { Fmt.ClockRange(_next.Start, _next.End), topic, _next.Subtitle }.Where(s => !string.IsNullOrEmpty(s)));
    }

    private void BuildToday(List<AgendaItem> today, DateTime now)
    {
        TodayRows.Clear();
        var nextIndex = today.FindIndex(i => i.Start > now);
        for (var i = 0; i < today.Count; i++)
        {
            var item = today[i];
            var past = item.End <= now;
            TodayRows.Add(new ScheduleRow(
                Fmt.Clock(item.Start), item.Title,
                $"{Fmt.ClockRange(item.Start, item.End)} · {item.Subtitle ?? _agenda.TopicName(item.TopicId)}",
                ColorMix.Brush(item.ColorHex ?? _agenda.TopicColor(item.TopicId)),
                past ? 0.6 : 1, past, i == nextIndex, $"Now · {Fmt.Clock(now)}"));
        }
        HasTodayRows = TodayRows.Count > 0;
    }

    private void BuildFocusWidgets(DateOnly today)
    {
        var sessions = _agenda.SessionRecords();
        var weekStart = WeekDays.StartOfWeek(today);
        var week = FocusStats.DailyMinutes(sessions, weekStart, 7);
        TodayFocusText = Fmt.HoursMinutes(week[WeekDays.Index(today.DayOfWeek)]);

        WeekBars.Clear();
        var max = Math.Max(120, week.Max());
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var fill = day == today ? "NerdyAccentBrush" : day < today ? "NerdyAccentTint30Brush" : "NerdyTrackBrush";
            WeekBars.Add(new WeekBar("MTWTFSS"[i].ToString(), Math.Max(6, week[i] / (double)max * 44), _theme.Brush(fill),
                $"{day:ddd, MMM d} · {Fmt.HoursMinutes(week[i])}"));
        }

        var streak = FocusStats.ComputeStreak(sessions, today);
        StreakText = streak.Current == 1 ? "1 day" : $"{streak.Current} days";
        StreakCells.Clear();
        var last = FocusStats.DailyMinutes(sessions, today.AddDays(-13), 14);
        for (var i = 0; i < 14; i++)
        {
            var minutes = last[i];
            var key = minutes == 0 ? "NerdyTrackBrush" : i == 13 ? "NerdyAccentBrush" : minutes >= 90 ? "NerdyAccentTint30Brush" : "NerdyAccentTint14Brush";
            StreakCells.Add(new StreakCell(_theme.Brush(key), $"{today.AddDays(i - 13):ddd, MMM d} · {Fmt.HoursMinutes(minutes)}"));
        }
    }

    private void BuildReminders(DateOnly today)
    {
        Reminders.Clear();
        foreach (var r in _agenda.DueToday().Take(4))
            Reminders.Add(ReminderItemViewModel.For(r, _data, _classes, _theme, _toasts, today));
        HasReminders = Reminders.Count > 0;

        Assignments.Clear();
        foreach (var a in _classes.MyAssignments.Where(a => !a.IsDone).OrderBy(a => a.DueAt).Take(4))
            Assignments.Add(ReminderItemViewModel.For(a, _data, _classes, _theme, _toasts, today));
    }

    private void BuildTopics()
    {
        Topics.Clear();
        foreach (var t in _data.Topics.Take(4))
            Topics.Add(new TopicCardViewModel(t.Id, t.Name, ColorMix.Brush(t.ColorHex), _agenda.TopicProgress(t.Id), _agenda.NextLabel(t.Id),
                id => _navigation.Navigate(NavSections.Topics, id)));
        HasTopics = Topics.Count > 0;
    }

    private void BuildAgenda(List<AgendaItem> today, DateTime now)
    {
        if (Hours.Count == 0)
            for (var h = AgendaStartHour; h <= AgendaEndHour; h++)
                Hours.Add(new HourLine(Fmt.HourLabel(h), (h - AgendaStartHour) * AgendaHourHeight));

        AgendaBlocks.Clear();
        foreach (var item in today)
        {
            var color = ColorMix.Parse(item.ColorHex ?? _agenda.TopicColor(item.TopicId));
            var startHour = Math.Max(AgendaStartHour, item.StartHour);
            var endHour = Math.Min(AgendaEndHour + 0.5, item.EndHour);
            if (endHour <= AgendaStartHour || startHour >= AgendaEndHour + 0.5) continue;
            AgendaBlocks.Add(new AgendaBlock(item,
                (startHour - AgendaStartHour) * AgendaHourHeight,
                Math.Max(28, (endHour - startHour) * AgendaHourHeight - 4),
                ColorMix.Brush(_theme.Tint(color, 16)), ColorMix.Brush(_theme.Ink(color)),
                item.End <= now ? 0.55 : 1, item == _next && item.Start > now, FocusOn));
        }
        var nowHour = now.TimeOfDay.TotalHours;
        ShowNowLine = nowHour >= AgendaStartHour && nowHour <= AgendaEndHour;
        NowTop = (nowHour - AgendaStartHour) * AgendaHourHeight - 5;
    }

    private void RefreshQuickFocus()
    {
        var mode = _focus.DraftMode;
        ModeSegments.Clear();
        foreach (var (m, label) in new[] { (FocusMode.Focus, "Focus"), (FocusMode.Ultra, "Ultra-Focus") })
            ModeSegments.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, m, c => _focus.DraftMode = (FocusMode)c.Value!, selected: m == mode));

        var seconds = _focus.DraftSeconds;
        DurationChips.Clear();
        foreach (var minutes in new[] { 15, 25, 45, 60, 90 })
            DurationChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, $"{minutes}m", minutes, c => _focus.DraftSeconds = (int)c.Value! * 60,
                selected: seconds == minutes * 60));
        DurationText = (seconds / 60).ToString();
        StartIcon = mode == FocusMode.Ultra ? Symbol.LockClosed : Symbol.Play;
        StartLabel = mode == FocusMode.Ultra ? $"Lock in for {Fmt.Duration(seconds)}" : $"Start {Fmt.Duration(seconds)} focus";
    }
}
