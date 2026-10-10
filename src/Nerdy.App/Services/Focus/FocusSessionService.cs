using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nerdy.App.Models;
using Nerdy.App.Services.Data;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;
using Nerdy.Focus;

namespace Nerdy.App.Services.Focus;

public sealed partial class PlanItem(string text) : ObservableObject
{
    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool Done { get; set; }
}

/// <summary>
/// Runs focus sessions: the shared draft (mode, duration, task, topic) used by Home and the Focus page, the timer,
/// app blocking through <see cref="FocusGuard"/>, Ultra-Focus window behaviour, and logging the outcome to history.
/// A small "active session" record is kept in the local store so a session cut short by a crash or by Task Manager
/// is recorded as <see cref="FocusOutcome.Interrupted"/> on the next launch.
/// </summary>
public sealed partial class FocusSessionService : ObservableObject
{
    private const string ActiveKey = "focus.active";

    /// <summary>
    /// Development only: NERDY_DEV_NO_LOCK=1 runs sessions without hiding/closing other apps or locking the window,
    /// so the session screens can be tested without disturbing the developer's desktop.
    /// </summary>
    private static readonly bool DevNoLock = Environment.GetEnvironmentVariable("NERDY_DEV_NO_LOCK") == "1";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private const int MinLoggedSeconds = 60;

    private readonly DataService _data;
    private readonly PreferencesService _preferences;
    private readonly NavigationService _navigation;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly IClock _clock;
    private readonly ILogger<FocusSessionService> _logger;
    private readonly Stopwatch _running = new();
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _timer;
    private FocusGuard? _guard;
    private TimeSpan _elapsedBeforePause;
    private DateTime _lastHeartbeat;
    private Guid _sessionId;

    public FocusSessionService(DataService data, PreferencesService preferences, NavigationService navigation, DialogService dialogs,
        ToastService toasts, IClock clock, ILogger<FocusSessionService> logger)
    {
        _data = data;
        _preferences = preferences;
        _navigation = navigation;
        _dialogs = dialogs;
        _toasts = toasts;
        _clock = clock;
        _logger = logger;
        DraftSeconds = preferences.Current.DefaultFocusMinutes * 60;
    }

    // ----- Draft (what the next session will be) -----
    [ObservableProperty] public partial FocusMode DraftMode { get; set; } = FocusMode.Focus;
    [ObservableProperty] public partial int DraftSeconds { get; set; }
    [ObservableProperty] public partial string DraftTask { get; set; } = "";
    [ObservableProperty] public partial Guid? DraftTopicId { get; set; }

    // ----- Active session -----
    [ObservableProperty] public partial bool IsActive { get; private set; }
    [ObservableProperty] public partial bool IsPaused { get; private set; }
    [ObservableProperty] public partial FocusMode Mode { get; private set; }
    [ObservableProperty] public partial string TaskTitle { get; private set; } = "";
    [ObservableProperty] public partial Guid? TopicId { get; private set; }
    [ObservableProperty] public partial int TotalSeconds { get; private set; }
    [ObservableProperty] public partial int RemainingSeconds { get; private set; }
    [ObservableProperty] public partial DateTime EndsAt { get; private set; }

    public ObservableCollection<PlanItem> Plan { get; } = [];
    public DateTimeOffset StartedAt { get; private set; }
    public int ElapsedSeconds => (int)(_elapsedBeforePause + _running.Elapsed).TotalSeconds;
    public double Progress => TotalSeconds == 0 ? 0 : Math.Clamp(ElapsedSeconds / (double)TotalSeconds, 0, 1);

    /// <summary>Allowed apps (enabled) for the dock and the guard.</summary>
    public IReadOnlyList<AllowedApp> AllowedApps => _preferences.Current.AllowedApps.Where(a => a.Enabled).ToList();
    public IReadOnlyList<string> AllowedWebsites => _preferences.Current.AllowedWebsites;

    /// <summary>Raised on the UI thread every second while a session runs.</summary>
    public event EventHandler? Tick;

    /// <summary>Raised when a session ends (any outcome), after it was logged.</summary>
    public event EventHandler<FocusSessionDto>? Ended;

    /// <summary>Starts a session from the draft. Returns false if the user cancelled (e.g. at the close-apps prompt).</summary>
    public async Task<bool> StartAsync()
    {
        if (IsActive) return true;
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        var prefs = _preferences.Current;
        var allowed = AllowedApps.Select(a => a.ExeName).ToList();

        if (prefs.BlockedApps == BlockedAppMode.Close && !DevNoLock && !await ConfirmClosingAsync(allowed)) return false;

        _sessionId = Guid.NewGuid();
        Mode = DraftMode;
        TaskTitle = string.IsNullOrWhiteSpace(DraftTask) ? "Focus session" : DraftTask.Trim();
        TopicId = DraftTopicId;
        TotalSeconds = Math.Max(60, DraftSeconds);
        RemainingSeconds = TotalSeconds;
        StartedAt = _clock.UtcNow;
        EndsAt = _clock.LocalNow.AddSeconds(TotalSeconds);
        _elapsedBeforePause = TimeSpan.Zero;
        _running.Restart();
        IsPaused = false;
        IsActive = true;
        await SaveHeartbeatAsync();

        _navigation.ShowRoot(Mode == FocusMode.Ultra ? RootView.UltraFocus : RootView.Focus);
        _timer ??= CreateTimer();
        _timer.Start();
        // Every session runs full screen; Ultra-Focus also stays on top of other windows.
        App.MainWindow.EnterSessionMode(alwaysOnTop: Mode == FocusMode.Ultra && !DevNoLock);
        if (DevNoLock)
        {
            _logger.LogWarning("NERDY_DEV_NO_LOCK is set: this session doesn't block apps or keep the window on top.");
            return true;
        }

        _guard = new FocusGuard(message => _logger.LogInformation("{Message}", message));
        _guard.AppBlocked += OnAppBlocked;
        try
        {
            await _guard.StartAsync(new FocusGuardOptions
            {
                Level = Mode == FocusMode.Ultra ? FocusLockLevel.Ultra : FocusLockLevel.Standard,
                BlockedAppAction = prefs.BlockedApps == BlockedAppMode.Close ? BlockedAppAction.Close : BlockedAppAction.Hide,
                AllowedExeNames = allowed,
                OwnerWindow = App.MainWindow.Handle
            });
        }
        catch (Exception ex)
        {
            // The timer still runs; blocking is best-effort.
            _logger.LogError(ex, "Couldn't start the focus guard");
            _toasts.Show("Couldn't hide other apps for this session.", ToastKind.Warning);
        }
        return true;
    }

    public void TogglePause()
    {
        if (!IsActive || Mode == FocusMode.Ultra) return;
        if (IsPaused)
        {
            _running.Restart();
            IsPaused = false;
            EndsAt = _clock.LocalNow.AddSeconds(RemainingSeconds);
        }
        else
        {
            _elapsedBeforePause += _running.Elapsed;
            _running.Reset();
            IsPaused = true;
        }
    }

    public void AddFiveMinutes()
    {
        if (!IsActive || Mode == FocusMode.Ultra) return;
        TotalSeconds += 300;
        Update();
        EndsAt = EndsAt.AddMinutes(5);
    }

    /// <summary>Ends a normal Focus session now (logged as ended early unless the time was up).</summary>
    public Task EndAsync() => Mode == FocusMode.Ultra ? Task.CompletedTask
        : FinishAsync(RemainingSeconds <= 0 ? FocusOutcome.Completed : FocusOutcome.EndedEarly, null, null);

    /// <summary>Leaves Ultra-Focus through the emergency exit; the reason is kept in history.</summary>
    public Task EmergencyExitAsync(ExitReason reason, string note) => FinishAsync(FocusOutcome.EmergencyExit, reason, note.Trim());

    /// <summary>Allows an app for the rest of the session (from the dock's "Allow an app" menu).</summary>
    public void AllowForSession(string exeName)
    {
        _guard?.UpdateAllowedApps([.. AllowedApps.Select(a => a.ExeName), exeName]);
    }

    public void Launch(AllowedApp app)
    {
        try { AppCatalog.Launch(LaunchTarget(app)); }
        catch (Exception ex) { _toasts.Show($"Couldn't open {app.Name}: {ex.Message}", ToastKind.Error); }
    }

    public void OpenWebsite(string site)
    {
        try { AppCatalog.Launch(site.Contains("://") ? site : "https://" + site); }
        catch (Exception ex) { _toasts.Show($"Couldn't open {site}: {ex.Message}", ToastKind.Error); }
    }

    /// <summary>Called when the window is asked to close. Returns true to keep it open.</summary>
    public bool ShouldBlockClose()
    {
        if (!IsActive) return false;
        if (Mode == FocusMode.Ultra)
        {
            _toasts.Show("Ultra-Focus is locked. Use Emergency exit if you really need to leave.", ToastKind.Warning);
            return true;
        }
        _ = FinishAsync(FocusOutcome.EndedEarly, null, null);
        return false;
    }

    /// <summary>Records a session that was running when Nerdy last closed unexpectedly.</summary>
    public async Task RecoverInterruptedAsync()
    {
        var json = await _data.GetMetaAsync(ActiveKey);
        if (json is null) return;
        await _data.SetMetaAsync(ActiveKey, null);
        try
        {
            var state = JsonSerializer.Deserialize<ActiveState>(json, DataService.Json);
            if (state is null || state.ElapsedSeconds < 60) return;
            Log(new FocusSessionDto
            {
                Id = state.Id,
                UpdatedAt = _clock.UtcNow,
                TopicId = state.TopicId,
                Task = state.Task,
                Mode = state.Mode,
                PlannedSeconds = state.PlannedSeconds,
                ActualSeconds = Math.Min(state.ElapsedSeconds, state.PlannedSeconds),
                StartedAt = state.StartedAt,
                EndedAt = state.StartedAt.AddSeconds(state.ElapsedSeconds),
                Outcome = state.ElapsedSeconds >= state.PlannedSeconds ? FocusOutcome.Completed : FocusOutcome.Interrupted
            });
            _toasts.Show($"Your last session (\"{state.Task}\") was cut short; {Fmt.Duration(state.ElapsedSeconds)} was saved to history.", ToastKind.Info);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unreadable active-session record");
        }
    }

    // ----- Internals -----

    private async Task<bool> ConfirmClosingAsync(IReadOnlyCollection<string> allowed)
    {
        var toClose = AppCatalog.GetAppsThatWouldBeBlocked(allowed);
        if (toClose.Count == 0) return true;
        var list = new StackPanel { Spacing = 6 };
        list.Children.Add(new TextBlock
        {
            Text = "These apps aren't on your allowed list and will be closed when the session starts. Save anything you're working on first.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["NerdySecondaryWrapStyle"]
        });
        foreach (var app in toClose.Take(12))
            list.Children.Add(new TextBlock { Text = $"•  {app.DisplayName}", Style = (Style)Application.Current.Resources["NerdyBodyStyle"] });
        if (toClose.Count > 12) list.Children.Add(new TextBlock { Text = $"…and {toClose.Count - 12} more" });

        var dialog = new ContentDialog
        {
            Title = $"Close {toClose.Count} app{(toClose.Count == 1 ? "" : "s")}?",
            Content = list,
            PrimaryButtonText = "Close apps and start",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await _dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    private DispatcherQueueTimer CreateTimer()
    {
        var timer = _dispatcher!.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = true;
        timer.Tick += (_, _) => OnTimer();
        return timer;
    }

    private void OnTimer()
    {
        if (!IsActive) return;
        var before = RemainingSeconds;
        Update();
        if (RemainingSeconds != before) Tick?.Invoke(this, EventArgs.Empty);
        if (RemainingSeconds <= 0)
        {
            _ = FinishAsync(FocusOutcome.Completed, null, null);
            return;
        }
        if (_clock.LocalNow - _lastHeartbeat >= HeartbeatInterval) _ = SaveHeartbeatAsync();
    }

    private void Update() => RemainingSeconds = Math.Max(0, TotalSeconds - ElapsedSeconds);

    private async Task FinishAsync(FocusOutcome outcome, ExitReason? reason, string? note)
    {
        if (!IsActive) return;
        IsActive = false;
        _timer?.Stop();
        _elapsedBeforePause += _running.Elapsed;
        _running.Reset();
        var actual = Math.Min((int)_elapsedBeforePause.TotalSeconds, TotalSeconds);

        if (_guard is not null)
        {
            _guard.AppBlocked -= OnAppBlocked;
            await _guard.StopAsync();
            _guard = null;
        }
        App.MainWindow.ExitSessionMode();
        await _data.SetMetaAsync(ActiveKey, null);

        var session = Log(new FocusSessionDto
        {
            Id = _sessionId,
            UpdatedAt = _clock.UtcNow,
            TopicId = TopicId,
            Task = TaskTitle,
            Mode = Mode,
            PlannedSeconds = TotalSeconds,
            ActualSeconds = actual,
            StartedAt = StartedAt,
            EndedAt = _clock.UtcNow,
            Outcome = outcome,
            ExitReason = reason,
            ExitNote = string.IsNullOrEmpty(note) ? null : note
        });
        Plan.Clear();

        _navigation.ShowRoot(RootView.Shell);
        _navigation.Navigate(NavSections.Home);
        _toasts.Show(outcome switch
        {
            FocusOutcome.Completed => "Session complete. Nice work.",
            FocusOutcome.EmergencyExit => "Ultra-Focus ended early. Reason saved to focus history.",
            _ when actual < MinLoggedSeconds => "Session ended. Sessions under a minute aren't saved.",
            _ => $"Session saved · {Fmt.Duration(actual / 60 * 60)} focused"
        }, outcome == FocusOutcome.Completed ? ToastKind.Success : ToastKind.Info);
        Ended?.Invoke(this, session);
    }

    /// <summary>Saves a finished session. Very short ones (started by mistake) are dropped, except emergency exits.</summary>
    private FocusSessionDto Log(FocusSessionDto session)
    {
        if (session.ActualSeconds < MinLoggedSeconds && session.Outcome != FocusOutcome.EmergencyExit) return session;
        return _data.Save(session);
    }

    private async Task SaveHeartbeatAsync()
    {
        _lastHeartbeat = _clock.LocalNow;
        var state = new ActiveState(_sessionId, TaskTitle, TopicId, Mode, TotalSeconds, ElapsedSeconds, StartedAt);
        await _data.SetMetaAsync(ActiveKey, JsonSerializer.Serialize(state, DataService.Json));
    }

    private void OnAppBlocked(object? sender, AppBlockedEventArgs e)
    {
        // Raised on the guard's thread.
        _dispatcher?.TryEnqueue(() =>
        {
            var what = e.Action == BlockedAppAction.Close ? "closed" : "hidden";
            _toasts.Show(e.Succeeded
                ? $"{e.App.DisplayName} was {what}. It isn't on your allowed list."
                : $"Windows didn't let Nerdy act on {e.App.DisplayName} (it may be running as administrator).",
                e.Succeeded ? ToastKind.Info : ToastKind.Warning);
        });
    }

    private static string LaunchTarget(AllowedApp app)
    {
        if (!string.IsNullOrEmpty(app.Path) && File.Exists(app.Path)) return app.Path;
        return app.ExeName switch
        {
            "calculatorapp.exe" => "calculator:",
            "msedge.exe" => "microsoft-edge:",
            "spotify.exe" => "spotify:",
            "onenote.exe" => "onenote:",
            "discord.exe" => "discord:",
            _ => app.ExeName
        };
    }

    private sealed record ActiveState(Guid Id, string Task, Guid? TopicId, FocusMode Mode, int PlannedSeconds, int ElapsedSeconds, DateTimeOffset StartedAt);
}
