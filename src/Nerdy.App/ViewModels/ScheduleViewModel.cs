using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Controls;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Scheduling;
using Nerdy.Core.Time;
using Windows.UI.Text;

namespace Nerdy.App.ViewModels;

/// <summary>An existing event (faded) or a planned session (dashed) in the week preview.</summary>
public sealed class PreviewBlock(double start, double end, string label, Brush fill, Brush stroke, Brush foreground, bool ghost) : ITimeSlot
{
    public double StartHour { get; } = start;
    public double EndHour { get; } = end;
    public int Lane => 0;
    public int Lanes => 1;
    public string Label { get; } = label;
    public Brush Fill { get; } = fill;
    public Brush Stroke { get; } = stroke;
    public Brush Foreground { get; } = foreground;
    public bool IsGhost { get; } = ghost;
    public double Opacity => IsGhost ? 1 : 0.55;
    public DoubleCollection Dash => IsGhost ? [3, 2] : [];
}

public sealed class PreviewDay
{
    public required string Header { get; init; }
    public required Brush Background { get; init; }
    public required IReadOnlyList<PreviewBlock> Blocks { get; init; }
    public required IReadOnlyList<LineSlot> Lines { get; init; }
}

public sealed partial class SuggestionChip(string label, Action pick)
{
    public string Label { get; } = label;

    [RelayCommand]
    private void Pick() => pick();
}

public sealed partial class PlanRowViewModel(Action skip, Action undo)
{
    public required string DateText { get; init; }
    public required string TimeText { get; init; }
    public required Symbol Icon { get; init; }
    public required Brush IconBrush { get; init; }
    public required string Status { get; init; }
    public required Brush PillBackground { get; init; }
    public required Brush PillForeground { get; init; }
    public required Brush Background { get; init; }
    public required Brush BorderBrush { get; init; }
    public required TextDecorations Decorations { get; init; }
    public required bool IsConflict { get; init; }
    public required bool CanUndo { get; init; }
    public required string Reason { get; init; }
    public required Brush ReasonBrush { get; init; }
    public required string SuggestionText { get; init; }
    public required IReadOnlyList<SuggestionChip> Suggestions { get; init; }
    public bool HasReason => Reason.Length > 0;

    [RelayCommand]
    private void Skip() => skip();

    [RelayCommand]
    private void Undo() => undo();
}

public sealed partial class ScheduleViewModel : ObservableObject, INavigationAware
{
    public const double PreviewHourHeight = 26;

    private static readonly (double Hours, string Label)[] Durations = [(0.5, "30 min"), (1, "1 hr"), (1.5, "1.5 hr"), (2, "2 hr")];

    private readonly AgendaService _agenda;
    private readonly DataService _data;
    private readonly PreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly ToastService _toasts;
    private readonly IClock _clock;
    private readonly Dictionary<DateOnly, SessionOverride> _overrides = [];
    private readonly HashSet<DayOfWeek> _days = [];
    private IReadOnlyList<PlannedSession> _plan = [];
    private Guid? _subjectId;
    private Guid? _topicId;
    private ScheduleScope _scope = ScheduleScope.Week;
    private int _offset; // weeks (or months) ahead of now
    private bool _customDuration;
    private bool _active;

    public ScheduleViewModel(AgendaService agenda, DataService data, PreferencesService preferences, ThemeService theme,
        NavigationService navigation, ToastService toasts, IClock clock)
    {
        _agenda = agenda;
        _data = data;
        _preferences = preferences;
        _theme = theme;
        _navigation = navigation;
        _toasts = toasts;
        _clock = clock;
        var today = clock.Today;
        _days.Add(today.DayOfWeek);
        StartOptions = BuildStartOptions();
        var nextHalfHour = TimeOnly.FromDateTime(clock.LocalNow).Add(TimeSpan.FromMinutes(60));
        Start = StartOptions.FirstOrDefault(o => o.Value >= new TimeOnly(nextHalfHour.Hour, nextHalfHour.Minute >= 30 ? 30 : 0)) ?? StartOptions[^1];
        Reminder = ReminderOptions.FirstOrDefault(r => r.Value == preferences.Current.DefaultReminderMinutes) ?? ReminderOptions[1];
    }

    public static readonly IReadOnlyList<Option<int?>> ReminderOptions =
        [new("No reminder", null), new("5 min before", 5), new("10 min before", 10), new("30 min before", 30), new("1 hour before", 60)];

    public IReadOnlyList<Option<TimeOnly>> StartOptions { get; }

    public ObservableCollection<ChoiceItem> SubjectChips { get; } = [];
    /// <summary>The picked subject's topics (optional).</summary>
    public ObservableCollection<ChoiceItem> TopicChips { get; } = [];
    [ObservableProperty] public partial bool HasTopicChips { get; private set; }
    public ObservableCollection<ChoiceItem> ScopeSegments { get; } = [];
    public ObservableCollection<ChoiceItem> DayChips { get; } = [];
    public ObservableCollection<ChoiceItem> DurationChips { get; } = [];
    public ObservableCollection<ChoiceItem> ModeChoices { get; } = [];
    public ObservableCollection<PreviewDay> PreviewDays { get; } = [];
    public ObservableCollection<PlanRowViewModel> Rows { get; } = [];
    public List<LineSlot> PreviewHours { get; } = [];
    public List<LineSlot> PreviewLines { get; } = [];

    [ObservableProperty] public partial string Title { get; set; } = "Study block";
    [ObservableProperty] public partial Option<TimeOnly> Start { get; set; }
    [ObservableProperty] public partial Option<int?> Reminder { get; set; }
    [ObservableProperty] public partial double DurationHours { get; set; } = 1.5;
    [ObservableProperty] public partial bool AutoFocus { get; set; } = true;
    [ObservableProperty] public partial FocusMode AutoFocusMode { get; set; } = FocusMode.Focus;

    [ObservableProperty] public partial string ScopeDescription { get; private set; } = "";
    [ObservableProperty] public partial string DaysLabel { get; private set; } = "";
    [ObservableProperty] public partial bool IsCustomDuration { get; private set; }
    [ObservableProperty] public partial double CustomHours { get; set; } = 1;
    [ObservableProperty] public partial double CustomMinutes { get; set; } = 45;
    [ObservableProperty] public partial string CustomSummary { get; private set; } = "";
    [ObservableProperty] public partial string PreviewTitle { get; private set; } = "";
    [ObservableProperty] public partial string PlanSummary { get; private set; } = "";
    [ObservableProperty] public partial string EmptyText { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    [ObservableProperty] public partial string SaveHint { get; private set; } = "";
    [ObservableProperty] public partial bool HasAttention { get; private set; }
    [ObservableProperty] public partial string SaveLabel { get; private set; } = "Add sessions";
    [ObservableProperty] public partial bool CanSave { get; private set; }
    [ObservableProperty] public partial bool CanGoBack { get; private set; }

    public double PreviewHeight => (_preferences.Current.StudyDayEndHour - _preferences.Current.StudyDayStartHour) * PreviewHourHeight;
    public double PreviewFirstHour => _preferences.Current.StudyDayStartHour;
    public double PreviewHourCount => _preferences.Current.StudyDayEndHour - _preferences.Current.StudyDayStartHour;

    // ----- Conflict dialog -----
    public IReadOnlyList<PlanRowViewModel> ConflictRows => Rows.Where(r => r.IsConflict).ToList();
    public string DialogTitle { get; private set; } = "";
    public string DialogPrimary { get; private set; } = "";
    public bool DialogClear => !HasAttention;

    /// <summary>Raised when saving needs the user to resolve clashes; the page shows the dialog.</summary>
    public event EventHandler? ConflictsNeedAttention;

    public void OnNavigatedTo(object? parameter)
    {
        switch (parameter)
        {
            case Guid subjectId: (_subjectId, _topicId) = (subjectId, null); Title = SuggestedTitle(); break;
            case StudyLink link: (_subjectId, _topicId) = (link.SubjectId, link.TopicId); Title = SuggestedTitle(); break;
        }
        _theme.Changed += OnThemeChanged;
        _data.Changed += OnDataChanged;
        _active = true;
        BuildStatic();
        Replan();
    }

    public void OnNavigatedFrom()
    {
        _active = false;
        _theme.Changed -= OnThemeChanged;
        _data.Changed -= OnDataChanged;
    }

    partial void OnStartChanged(Option<TimeOnly> value) => ResetAndReplan();
    partial void OnDurationHoursChanged(double value) => ResetAndReplan();
    partial void OnReminderChanged(Option<int?> value) => Replan();
    partial void OnTitleChanged(string value) => Replan();

    partial void OnCustomHoursChanged(double value) => ApplyCustom();
    partial void OnCustomMinutesChanged(double value) => ApplyCustom();

    [RelayCommand]
    private void Cancel() => _navigation.Navigate(NavSections.Calendar);

    [RelayCommand]
    private void PreviousRange()
    {
        if (_offset == 0) return;
        _offset--;
        ResetAndReplan(rebuildDays: true);
    }

    [RelayCommand]
    private void NextRange()
    {
        _offset++;
        ResetAndReplan(rebuildDays: true);
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave) return;
        if (HasAttention) ConflictsNeedAttention?.Invoke(this, EventArgs.Empty);
        else Commit();
    }

    public void SkipConflictsAndAdd()
    {
        foreach (var (k, v) in SchedulePlanner.WithConflictsSkipped(_plan, _overrides)) _overrides[k] = v;
        Replan();
        Commit();
    }

    public void UseSuggestionsAndAdd()
    {
        foreach (var (k, v) in SchedulePlanner.WithFirstSuggestions(_plan, _overrides)) _overrides[k] = v;
        Replan();
        // Suggestions only use free slots, but earlier sessions may now take a later one's suggestion; skip any leftovers.
        if (HasAttention)
        {
            foreach (var (k, v) in SchedulePlanner.WithConflictsSkipped(_plan, _overrides)) _overrides[k] = v;
            Replan();
        }
        Commit();
    }

    private void Commit()
    {
        var ready = _plan.Where(p => p.WillBeAdded).ToList();
        if (ready.Count == 0) return;
        var skipped = _plan.Count - ready.Count;
        var series = Guid.NewGuid();
        var subtitle = _data.Subject(_subjectId) is null ? null : _agenda.LinkLabel(_subjectId, _topicId);
        var title = string.IsNullOrWhiteSpace(Title) ? "Study block" : Title.Trim();
        _data.SaveMany(ready.Select(p => new CalendarEventDto
        {
            Id = Guid.NewGuid(),
            UpdatedAt = DateTimeOffset.UtcNow,
            Title = title,
            Subtitle = subtitle,
            SubjectId = _subjectId,
            TopicId = _subjectId is null ? null : _topicId,
            StartsAt = new DateTimeOffset(p.StartDateTime),
            EndsAt = new DateTimeOffset(p.EndDateTime),
            ReminderMinutes = Reminder.Value,
            AutoFocus = AutoFocus,
            AutoFocusMode = AutoFocusMode,
            SeriesId = series
        }));
        _toasts.Show($"Added {ready.Count} session{(ready.Count == 1 ? "" : "s")}{(skipped > 0 ? $" · {skipped} skipped" : "")}", ToastKind.Success);
        _overrides.Clear();
        _navigation.Navigate(NavSections.Calendar, ready[0].Date);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        BuildStatic();
        Replan();
    }

    private void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.FromSync && e.Affects(DataKinds.Events | DataKinds.Subjects | DataKinds.Topics)) { BuildStatic(); Replan(); }
    }

    private void ResetAndReplan(bool rebuildDays = false)
    {
        _overrides.Clear();
        if (rebuildDays) BuildDays();
        Replan();
    }

    private void BuildStatic()
    {
        SubjectChips.Clear();
        foreach (var subject in _data.Subjects)
            SubjectChips.Add(new ChoiceItem(_theme, ChoiceLook.ColorChip, subject.Name, subject.Id, PickSubject, colorHex: subject.ColorHex,
                selected: subject.Id == _subjectId));
        RebuildTopicChips();

        ScopeSegments.Clear();
        foreach (var (scope, label, icon) in new[] { (ScheduleScope.Once, "One session", Symbol.CalendarDay), (ScheduleScope.Week, "Whole week", Symbol.CalendarWeekNumbers), (ScheduleScope.Month, "Whole month", Symbol.CalendarMonth) })
            ScopeSegments.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, scope, PickScope, icon, selected: scope == _scope));

        DurationChips.Clear();
        foreach (var (hours, label) in Durations)
            DurationChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, label, hours, PickDuration, selected: !_customDuration && Math.Abs(DurationHours - hours) < 0.001));
        DurationChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, "Custom", null, PickDuration, Symbol.Timer, selected: _customDuration));

        ModeChoices.Clear();
        foreach (var (mode, label, icon) in new[] { (FocusMode.Focus, "Focus", Symbol.TargetArrow), (FocusMode.Ultra, "Ultra-Focus", Symbol.ShieldLock) })
            ModeChoices.Add(new ChoiceItem(_theme, ChoiceLook.Chip, label, mode, PickMode, icon, selected: mode == AutoFocusMode));

        if (PreviewHours.Count == 0)
            for (var h = 0; h <= PreviewHourCount; h += 2)
            {
                PreviewHours.Add(new LineSlot(PreviewFirstHour + h, Fmt.HourLabel((int)PreviewFirstHour + h)));
            }
        if (PreviewLines.Count == 0)
            for (var h = 0; h <= PreviewHourCount; h += 2) PreviewLines.Add(new LineSlot(PreviewFirstHour + h, ""));
        BuildDays();
    }

    private void BuildDays()
    {
        var today = _clock.Today;
        var week = WeekDays.StartOfWeek(Anchor());
        DayChips.Clear();
        foreach (var d in WeekDays.MondayFirst)
        {
            var past = _scope != ScheduleScope.Month && week.AddDays(WeekDays.Index(d)) < today;
            var chip = new ChoiceItem(_theme, ChoiceLook.Chip, d.ToString()[..1], d, ToggleDay, selected: _days.Contains(d) && !past)
            {
                IsEnabled = !past,
                ToolTip = past ? "Already passed this week" : d.ToString()
            };
            DayChips.Add(chip);
        }
        DaysLabel = _scope switch { ScheduleScope.Once => "Day", ScheduleScope.Week => "Days this week", _ => "Every week on" };
        ScopeDescription = _scope switch
        {
            ScheduleScope.Once => "A single session on the day you pick.",
            ScheduleScope.Week => "One session on each selected day for the rest of the week.",
            _ => $"Repeats on the selected weekdays through {WeekDays.EndOfMonth(Anchor()):MMM d}. Each date is checked for clashes."
        };
    }

    /// <summary>A date inside the week (or month) being planned: today, or the start of a later week/month.</summary>
    private DateOnly Anchor()
    {
        var today = _clock.Today;
        if (_offset == 0) return today;
        return _scope == ScheduleScope.Month ? new DateOnly(today.Year, today.Month, 1).AddMonths(_offset) : today.AddDays(7 * _offset);
    }

    private void PickSubject(ChoiceItem item)
    {
        var id = (Guid)item.Value!;
        var suggested = SuggestedTitle();
        _subjectId = _subjectId == id ? null : id;
        _topicId = null;
        foreach (var c in SubjectChips) c.IsSelected = (Guid)c.Value! == _subjectId;
        RebuildTopicChips();
        // Keep a title the user typed; replace one we suggested.
        if (Title == suggested) Title = SuggestedTitle();
    }

    private void PickTopic(ChoiceItem item)
    {
        var id = (Guid)item.Value!;
        var suggested = SuggestedTitle();
        _topicId = _topicId == id ? null : id;
        foreach (var c in TopicChips) c.IsSelected = (Guid)c.Value! == _topicId;
        if (Title == suggested) Title = SuggestedTitle();
    }

    private void RebuildTopicChips()
    {
        TopicChips.Clear();
        if (_data.Subject(_subjectId) is { } subject)
            foreach (var t in _data.TopicsOf(subject.Id))
                TopicChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, t.Name, t.Id, PickTopic, selected: t.Id == _topicId));
        HasTopicChips = TopicChips.Count > 0;
    }

    /// <summary>"Cell biology study block", "Biology study block", or "Study block".</summary>
    private string SuggestedTitle() =>
        _data.Topic(_topicId) is { } t && t.SubjectId == _subjectId ? $"{t.Name} study block"
        : _data.Subject(_subjectId) is { } s ? $"{s.Name} study block"
        : "Study block";

    private void PickScope(ChoiceItem item)
    {
        _scope = (ScheduleScope)item.Value!;
        _offset = 0;
        foreach (var c in ScopeSegments) c.IsSelected = c == item;
        if (_scope == ScheduleScope.Once && _days.Count > 1)
        {
            var keep = _days.Where(d => d >= _clock.Today.DayOfWeek || d == DayOfWeek.Sunday).OrderBy(WeekDays.Index).FirstOrDefault(_days.First());
            _days.Clear();
            _days.Add(keep);
        }
        ResetAndReplan(rebuildDays: true);
    }

    private void ToggleDay(ChoiceItem item)
    {
        var day = (DayOfWeek)item.Value!;
        if (_scope == ScheduleScope.Once)
        {
            _days.Clear();
            _days.Add(day);
        }
        else if (!_days.Remove(day)) _days.Add(day);
        foreach (var c in DayChips) c.IsSelected = _days.Contains((DayOfWeek)c.Value!) && c.IsEnabled;
        ResetAndReplan();
    }

    private void PickDuration(ChoiceItem item)
    {
        _customDuration = item.Value is null;
        IsCustomDuration = _customDuration;
        foreach (var c in DurationChips) c.IsSelected = c == item;
        if (_customDuration)
        {
            CustomHours = Math.Floor(DurationHours);
            CustomMinutes = Math.Round((DurationHours - Math.Floor(DurationHours)) * 60);
            ApplyCustom();
        }
        else DurationHours = (double)item.Value!;
    }

    private void ApplyCustom()
    {
        if (!_customDuration) return;
        var minutes = Math.Clamp((double.IsNaN(CustomHours) ? 0 : CustomHours) * 60 + (double.IsNaN(CustomMinutes) ? 0 : CustomMinutes), 5, 12 * 60);
        DurationHours = minutes / 60.0;
        CustomSummary = $"{Fmt.Duration((int)(minutes * 60))} · {Fmt.Clock(Start.Value)} – {Fmt.Clock(Start.Value.AddMinutes(minutes))}";
    }

    private void PickMode(ChoiceItem item)
    {
        AutoFocusMode = (FocusMode)item.Value!;
        foreach (var c in ModeChoices) c.IsSelected = c == item;
    }

    private SchedulePlanner Planner() => new(_agenda.BusyOn, new StudyDay
    {
        Start = new TimeOnly(_preferences.Current.StudyDayStartHour, 0),
        End = new TimeOnly(_preferences.Current.StudyDayEndHour, 0)
    });

    private StudyBlockRequest Request() => new()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? "Study block" : Title.Trim(),
        Scope = _scope,
        Days = _days.ToList(),
        Start = Start.Value,
        Duration = TimeSpan.FromHours(DurationHours),
        Anchor = Anchor()
    };

    private void Replan()
    {
        if (!_active) return;
        var now = _clock.LocalNow;
        _plan = Planner().Plan(Request(), _overrides, now);
        CanGoBack = _offset > 0;
        BuildRows();
        BuildPreview(now);
        UpdateFooter();
    }

    private void BuildRows()
    {
        Rows.Clear();
        foreach (var p in _plan)
        {
            var conflict = p.Status == PlanStatus.Conflict;
            var moved = p.Status == PlanStatus.Moved;
            var skipped = p.Status == PlanStatus.Skipped;
            var key = p.Key;
            Brush B(string k) => _theme.Brush(k);
            Rows.Add(new PlanRowViewModel(() => SetOverride(key, new SessionOverride.Skip()), () => SetOverride(key, null))
            {
                DateText = $"{p.Date:ddd, MMM d}",
                TimeText = $"{Fmt.Clock(p.Start)} – {Fmt.Clock(p.Start.Add(p.Duration))}",
                Icon = conflict ? (p.DayFull ? Symbol.CalendarError : Symbol.Warning) : skipped ? Symbol.DismissCircle : Symbol.CheckmarkCircle,
                IconBrush = B(conflict ? "NerdyDangerBrush" : skipped ? "NerdyTextTertiaryBrush" : moved ? "NerdyAccentBrush" : "NerdySuccessBrush"),
                Status = conflict ? (p.WeekFull ? "Week full" : p.DayFull ? "Day full" : "Conflict") : moved ? "Moved" : skipped ? "Skipped" : "Free",
                PillBackground = B(conflict ? "NerdyDangerSoftBrush" : moved ? "NerdyAccentTint14Brush" : skipped ? "NerdySubtleFillBrush" : "NerdySuccessSoftBrush"),
                PillForeground = B(conflict ? "NerdyDangerTextBrush" : moved ? "NerdyAccentTextBrush" : skipped ? "NerdyTextSecondaryBrush" : "NerdySuccessTextBrush"),
                Background = B(conflict ? "NerdyDangerCardBrush" : "NerdyCardBackgroundBrush"),
                BorderBrush = B(conflict ? "NerdyDangerBorderBrush" : "NerdyCardBorderBrush"),
                Decorations = skipped ? TextDecorations.Strikethrough : TextDecorations.None,
                IsConflict = conflict,
                CanUndo = moved || skipped,
                Reason = conflict ? Reason(p) : moved ? $"Moved from {p.Key:ddd, MMM d}, {Fmt.Clock(Start.Value)}" : "",
                ReasonBrush = B(conflict ? "NerdyDangerTextBrush" : "NerdyTextSecondaryBrush"),
                SuggestionText = !conflict ? "" : p.Suggestions.Count == 0 ? "No replacement found." : p.WeekFull ? "Next week:" : p.DayFull ? "Try another day:" : "Free nearby:",
                Suggestions = p.Suggestions.Select(s => new SuggestionChip(
                    s.Date == p.Date ? Fmt.Clock(s.Start) : $"{s.Date:ddd d} · {Fmt.Clock(s.Start)}",
                    () => SetOverride(key, new SessionOverride.MoveTo(s.Date, s.Start)))).ToList()
            });
        }
        OnPropertyChanged(nameof(ConflictRows));
    }

    private string Reason(PlannedSession p)
    {
        var duration = Fmt.Duration(p.Duration);
        if (p.WeekFull) return $"No free {duration} slot left this week.";
        if (p.DayFull) return $"{p.Date:dddd, MMM d} is fully booked{(p.ConflictWith is { } c ? $" ({c.Title})" : "")}.";
        return p.Conflict switch
        {
            ConflictKind.InPast => "That time has already passed.",
            ConflictKind.OutsideStudyDay => $"Runs past the end of your study day ({Fmt.HourLabel(_preferences.Current.StudyDayEndHour)}).",
            _ => $"Overlaps {p.ConflictWith?.Title} · {Fmt.ClockRange(p.ConflictWith!.Start, p.ConflictWith.End)}"
        };
    }

    private void BuildPreview(DateTime now)
    {
        var week = WeekDays.StartOfWeek(_plan.FirstOrDefault()?.Date ?? Anchor());
        PreviewTitle = $"{(week == WeekDays.StartOfWeek(_clock.Today) ? "This week" : "Week")} · {week:MMM d} – {week.AddDays(6):MMM d}";
        PreviewDays.Clear();
        var today = _clock.Today;
        for (var i = 0; i < 7; i++)
        {
            var date = week.AddDays(i);
            var blocks = new List<PreviewBlock>();
            foreach (var item in _agenda.On(date).Where(x => !x.IsAllDay))
            {
                var color = ColorMix.Parse(item.ColorHex ?? _agenda.SubjectColor(item.SubjectId));
                blocks.Add(new PreviewBlock(item.StartHour, item.EndHour, "", ColorMix.Brush(_theme.Tint(color, 30)), _theme.Brush("NerdyTransparentBrush"), _theme.Brush("NerdyTextPrimaryBrush"), false));
            }
            foreach (var p in _plan.Where(p => p.Status != PlanStatus.Skipped && p.Date == date))
            {
                var c = p.Status == PlanStatus.Conflict;
                var startHour = p.Start.ToTimeSpan().TotalHours;
                blocks.Add(new PreviewBlock(startHour, startHour + p.Duration.TotalHours, string.IsNullOrWhiteSpace(Title) ? "Study block" : Title,
                    _theme.Brush(c ? "NerdyDangerSoftBrush" : "NerdyAccentTint14Brush"),
                    _theme.Brush(c ? "NerdyDangerBrush" : "NerdyAccentBrush"),
                    _theme.Brush(c ? "NerdyDangerTextBrush" : "NerdyAccentTextBrush"), true));
            }
            PreviewDays.Add(new PreviewDay
            {
                Header = $"{date:ddd} {date.Day}",
                Background = _theme.Brush(date < today ? "NerdyWeekendColumnBrush" : "NerdyTransparentBrush"),
                Blocks = blocks,
                Lines = PreviewLines
            });
        }
    }

    private void UpdateFooter()
    {
        var n = _plan.Count;
        var attention = _plan.Count(p => p.Status == PlanStatus.Conflict);
        var skipped = _plan.Count(p => p.Status == PlanStatus.Skipped);
        var ready = n - attention - skipped;
        IsEmpty = n == 0;
        EmptyText = _days.Count == 0
            ? "Pick at least one day."
            : "The days you picked have already passed. Pick today or later, move to next week, or schedule the whole month.";
        PlanSummary = n == 0 ? "" :
            $"{n} session{(n == 1 ? "" : "s")} · {ready} ready{(attention > 0 ? $" · {attention} need{(attention == 1 ? "s" : "")} attention" : "")}{(skipped > 0 ? $" · {skipped} skipped" : "")}";
        HasAttention = attention > 0;
        SaveHint = attention > 0
            ? $"{attention} session{(attention == 1 ? "" : "s")} clash{(attention == 1 ? "es" : "")} with your schedule"
            : n > 0 ? $"{Fmt.Clock(Start.Value)} – {Fmt.Clock(Start.Value.Add(TimeSpan.FromHours(DurationHours)))}{(Reminder.Value is { } m ? $" · reminder {m} min before" : "")}" : "";
        CanSave = ready + attention > 0;
        SaveLabel = _scope == ScheduleScope.Once ? "Add session" : $"Add {ready + attention} session{(ready + attention == 1 ? "" : "s")}";

        var full = _plan.Count(p => p.Status == PlanStatus.Conflict && p.DayFull);
        DialogTitle = attention == 0 ? "Ready to add"
            : full > 0 ? (full == attention ? (attention > 1 ? "Some days are fully booked" : "A day is fully booked") : "Some sessions don't fit")
            : $"{attention} session{(attention == 1 ? "" : "s")} clash{(attention == 1 ? "es" : "")} with your schedule";
        DialogPrimary = attention > 0 ? "Use first suggestions & add" : $"Add {ready} session{(ready == 1 ? "" : "s")}";
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(DialogPrimary));
        OnPropertyChanged(nameof(DialogClear));
    }

    private void SetOverride(DateOnly key, SessionOverride? value)
    {
        if (value is null) _overrides.Remove(key);
        else _overrides[key] = value;
        Replan();
    }

    private IReadOnlyList<Option<TimeOnly>> BuildStartOptions()
    {
        var p = _preferences.Current;
        var list = new List<Option<TimeOnly>>();
        for (var t = new TimeSpan(p.StudyDayStartHour, 0, 0); t < new TimeSpan(p.StudyDayEndHour, 0, 0); t += TimeSpan.FromMinutes(30))
            list.Add(new Option<TimeOnly>(Fmt.Clock(TimeOnly.FromTimeSpan(t)), TimeOnly.FromTimeSpan(t)));
        return list;
    }
}
