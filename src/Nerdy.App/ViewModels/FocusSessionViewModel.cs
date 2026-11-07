using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Theming;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;

namespace Nerdy.App.ViewModels;

public sealed partial class DockItem(string name, Symbol icon, Action open)
{
    public string Name { get; } = name;
    public Symbol Icon { get; } = icon;

    [RelayCommand]
    private void Open() => open();
}

/// <summary>Shared by the Focus and Ultra-Focus session screens.</summary>
public sealed partial class FocusSessionViewModel : ObservableObject, INavigationAware
{
    private const int MinExitExplanation = 15;
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(3);

    private readonly FocusSessionService _focus;
    private readonly AgendaService _agenda;
    private readonly DataService _data;
    private readonly PreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly ToastService _toasts;
    private readonly IClock _clock;
    private readonly ApiClient _api;
    private DispatcherQueueTimer? _holdTimer;
    private DateTime _holdStarted;

    public FocusSessionViewModel(FocusSessionService focus, AgendaService agenda, DataService data, PreferencesService preferences,
        ThemeService theme, ToastService toasts, IClock clock, ApiClient api)
    {
        _api = api;
        _focus = focus;
        _agenda = agenda;
        _data = data;
        _preferences = preferences;
        _theme = theme;
        _toasts = toasts;
        _clock = clock;
    }

    public ObservableCollection<DockItem> Dock { get; } = [];
    public ObservableCollection<PlanItem> Plan => _focus.Plan;
    public IReadOnlyList<string> ExitReasonLabels { get; } =
        ["Medical or safety emergency", "Family emergency", "Urgent school or work matter", "Something else"];

    private static readonly ExitReason[] ExitReasonValues = [ExitReason.Medical, ExitReason.Family, ExitReason.Urgent, ExitReason.Other];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanHold), nameof(ExitHint), nameof(HoldOpacity), nameof(ExitPlaceholder))]
    public partial int ExitReasonIndex { get; set; } = -1;

    [ObservableProperty] public partial string TaskTitle { get; private set; } = "";
    /// <summary>"Biology · Cell biology", "Biology", or "General".</summary>
    [ObservableProperty] public partial string SubjectName { get; private set; } = "";
    [ObservableProperty] public partial Brush? SubjectBrush { get; private set; }
    [ObservableProperty] public partial string TimeText { get; private set; } = "";
    [ObservableProperty] public partial string SubText { get; private set; } = "";
    [ObservableProperty] public partial string EndsText { get; private set; } = "";
    [ObservableProperty] public partial double Remaining { get; private set; } = 1;
    [ObservableProperty] public partial double ElapsedPercent { get; private set; }
    [ObservableProperty] public partial bool IsPaused { get; private set; }
    [ObservableProperty] public partial Symbol PauseIcon { get; private set; } = Symbol.Pause;
    [ObservableProperty] public partial string PauseLabel { get; private set; } = "Pause";
    [ObservableProperty] public partial bool IsAgendaLayout { get; private set; }
    [ObservableProperty] public partial string Wallpaper { get; private set; } = "bloom";
    [ObservableProperty] public partial bool HasUpNext { get; private set; }
    [ObservableProperty] public partial string UpNextTitle { get; private set; } = "";
    [ObservableProperty] public partial string UpNextTime { get; private set; } = "";
    [ObservableProperty] public partial Brush? UpNextBrush { get; private set; }
    [ObservableProperty] public partial string NewPlanItem { get; set; } = "";
    [ObservableProperty] public partial string QuickNote { get; set; } = "";
    [ObservableProperty] public partial string AllowedText { get; private set; } = "";

    // ----- Ultra-Focus emergency exit -----
    [ObservableProperty] public partial bool IsExitOpen { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanHold), nameof(ExitHint), nameof(HoldOpacity))]
    public partial string ExitText { get; set; } = "";
    [ObservableProperty] public partial double HoldPercent { get; private set; }
    [ObservableProperty] public partial string HoldLabel { get; private set; } = "Hold to exit";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanHold), nameof(HoldOpacity))]
    public partial bool IsReviewing { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasReviewMessage))]
    public partial string? ReviewMessage { get; private set; }

    public bool HasReviewMessage => !string.IsNullOrEmpty(ReviewMessage);

    private ExitReason? SelectedReason => ExitReasonIndex >= 0 ? ExitReasonValues[ExitReasonIndex] : null;

    /// <summary>Only non-emergency reasons go to the AI review, and only when the user keeps it switched on.</summary>
    private bool WillBeReviewed => SelectedReason is ExitReason.Urgent or ExitReason.Other && _preferences.Current.AiExitReview;

    /// <summary>A medical or safety emergency needs only the hold; every other reason needs a short explanation.</summary>
    public bool CanHold => !IsReviewing && SelectedReason is { } reason &&
                           (reason == ExitReason.Medical || ExitText.Trim().Length >= MinExitExplanation);

    public double HoldOpacity => CanHold ? 1 : 0.45;

    public string ExitPlaceholder => SelectedReason == ExitReason.Medical ? "Optional: add a note for your history" : "Briefly describe what happened";

    private const string ReviewNotice = "When you hold to exit, Claude (AI) reads your explanation and may suggest staying a little longer.";

    public string ExitHint => SelectedReason switch
    {
        null => "Pick a reason first.",
        ExitReason.Medical => "Hold the exit button for 3 seconds to leave. A note is optional.",
        // Say up front (before anything is typed) that the explanation goes to Claude.
        _ when ExitText.Trim().Length < MinExitExplanation =>
            $"Write at least {MinExitExplanation} characters ({ExitText.Trim().Length}/{MinExitExplanation}).{(WillBeReviewed ? " " + ReviewNotice : "")}",
        _ when WillBeReviewed => ReviewNotice,
        _ => "Hold the exit button for 3 seconds to leave."
    };

    public void OnNavigatedTo(object? parameter)
    {
        _focus.Tick += OnTick;
        _focus.PropertyChanged += OnFocusChanged;
        var prefs = _preferences.Current;
        IsAgendaLayout = prefs.HomeLayout == HomeLayout.Agenda;
        Wallpaper = prefs.Wallpaper;
        TaskTitle = _focus.TaskTitle;
        SubjectName = _agenda.LinkLabel(_focus.SubjectId, _focus.TopicId);
        SubjectBrush = ColorMix.Brush(_data.Subject(_focus.SubjectId)?.ColorHex ?? ColorMix.ToHex(_theme.Accent));
        BuildDock();
        BuildUpNext();
        Update();
    }

    public void OnNavigatedFrom()
    {
        _focus.Tick -= OnTick;
        _focus.PropertyChanged -= OnFocusChanged;
        _holdTimer?.Stop();
        SaveQuickNote();
    }

    [RelayCommand] private void TogglePause() => _focus.TogglePause();
    [RelayCommand] private void AddFive() => _focus.AddFiveMinutes();
    [RelayCommand] private Task EndAsync() => _focus.EndAsync();

    [RelayCommand]
    private void AddPlanItem()
    {
        var text = NewPlanItem.Trim();
        if (text.Length == 0) return;
        _focus.Plan.Add(new PlanItem(text));
        NewPlanItem = "";
    }

    [RelayCommand]
    private void OpenExit()
    {
        IsExitOpen = true;
        HoldPercent = 0;
        HoldLabel = "Hold to exit";
        ReviewMessage = null;
    }

    partial void OnExitReasonIndexChanged(int value) => ReviewMessage = null;

    [RelayCommand]
    private void CloseExit()
    {
        _holdTimer?.Stop();
        IsExitOpen = false;
        HoldPercent = 0;
    }

    public void StartHold()
    {
        if (!CanHold) return;
        _holdStarted = _clock.LocalNow;
        _holdTimer ??= CreateHoldTimer();
        _holdTimer.Start();
        HoldLabel = "Keep holding…";
    }

    public void StopHold()
    {
        if (_holdTimer is null || !_holdTimer.IsRunning) return;
        _holdTimer.Stop();
        HoldPercent = 0;
        HoldLabel = "Hold to exit";
    }

    public void SaveQuickNote()
    {
        var text = QuickNote.Trim();
        if (text.Length == 0) return;
        var title = $"Session notes · {_focus.TaskTitle}";
        var existing = _data.Notes.FirstOrDefault(n => n.Title == title);
        var line = $"{Fmt.Clock(_clock.LocalNow)}  {text}";
        if (existing is null)
            _data.Save(new NoteDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, Title = title, SubjectId = _focus.SubjectId, TopicId = _focus.TopicId, BodyText = line });
        else
            _data.Save(existing with { BodyText = existing.BodyText + "\n" + line, BodyRtf = "" });
        QuickNote = "";
        _toasts.Show("Saved to your notes", ToastKind.Success);
    }

    private DispatcherQueueTimer CreateHoldTimer()
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.Tick += async (_, _) =>
        {
            var p = (_clock.LocalNow - _holdStarted) / HoldDuration * 100;
            HoldPercent = Math.Min(100, p);
            if (p < 100) return;
            timer.Stop();
            await CompleteExitAsync();
        };
        return timer;
    }

    private async Task CompleteExitAsync()
    {
        if (SelectedReason is not { } reason) return;
        if (WillBeReviewed)
        {
            IsReviewing = true;
            HoldLabel = "Checking…";
            var review = await ReviewAsync(reason);
            IsReviewing = false;
            HoldPercent = 0;
            HoldLabel = "Hold to exit";
            if (!IsExitOpen) return; // they chose "Stay focused" while the review was running
            if (!review.Allowed)
            {
                ReviewMessage = string.IsNullOrWhiteSpace(review.Message)
                    ? "This sounds like it can wait until your session ends."
                    : review.Message;
                return;
            }
        }
        IsExitOpen = false;
        await _focus.EmergencyExitAsync(reason, ExitText);
    }

    /// <summary>Asks the server's AI review. Any failure (offline, timeout, server error) lets the user leave.</summary>
    private async Task<ExitReviewResponse> ReviewAsync(ExitReason reason)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            return await _api.ReviewExitAsync(new ExitReviewRequest
            {
                Reason = reason,
                Explanation = ExitText.Trim(),
                Task = _focus.TaskTitle,
                MinutesLeft = (int)Math.Ceiling(_focus.RemainingSeconds / 60.0),
                PlannedMinutes = _focus.TotalSeconds / 60
            }, timeout.Token);
        }
        catch (Exception ex) when (ex is OfflineException or ApiException or OperationCanceledException)
        {
            return new ExitReviewResponse { Allowed = true, Reviewed = false };
        }
    }

    private void OnTick(object? sender, EventArgs e) => Update();

    private void OnFocusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSessionService.IsPaused) or nameof(FocusSessionService.TotalSeconds) or nameof(FocusSessionService.EndsAt)) Update();
    }

    private void Update()
    {
        var total = Math.Max(1, _focus.TotalSeconds);
        var remaining = _focus.RemainingSeconds;
        TimeText = Fmt.Timer(remaining);
        Remaining = remaining / (double)total;
        ElapsedPercent = 100 - Remaining * 100;
        IsPaused = _focus.IsPaused;
        PauseIcon = IsPaused ? Symbol.Play : Symbol.Pause;
        PauseLabel = IsPaused ? "Resume" : "Pause";
        var ends = IsPaused ? "paused" : $"ends {Fmt.Clock(_focus.EndsAt)}";
        SubText = $"of {Fmt.Duration(total)} · {ends}";
        EndsText = _focus.Mode == FocusMode.Ultra ? $"Locked until {Fmt.Clock(_focus.EndsAt)}" : IsPaused ? "Paused" : $"Ends {Fmt.Clock(_focus.EndsAt)}";
    }

    private void BuildDock()
    {
        Dock.Clear();
        foreach (var app in _focus.AllowedApps)
            Dock.Add(new DockItem(app.Name, AppIcons.For(app.ExeName), () => _focus.Launch(app)));
        foreach (var site in _focus.AllowedWebsites)
            Dock.Add(new DockItem(site, Symbol.Link, () => _focus.OpenWebsite(site)));
        AllowedText = $"{_focus.AllowedApps.Count} allowed app{(_focus.AllowedApps.Count == 1 ? "" : "s")}";
    }

    private void BuildUpNext()
    {
        var next = _agenda.Between(_focus.EndsAt, _clock.Today.AddDays(1).ToDateTime(TimeOnly.MinValue)).FirstOrDefault(i => !i.IsAllDay);
        HasUpNext = next is not null;
        if (next is null) return;
        UpNextTitle = next.Title;
        UpNextTime = Fmt.Clock(next.Start);
        UpNextBrush = ColorMix.Brush(next.ColorHex ?? _agenda.SubjectColor(next.SubjectId));
    }

}
