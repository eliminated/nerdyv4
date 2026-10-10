using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Nerdy.App.Models;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Notifications;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;

namespace Nerdy.App.Services.Shell;

/// <summary>
/// Owns the main window's lifetime. With <see cref="Preferences.KeepRunningInTray"/> on (and someone signed in),
/// closing the window hides Nerdy to the notification area so reminders keep arriving; the tray icon reopens it or
/// quits. Ultra-Focus can't be closed or quit (only emergency exit ends it).
/// </summary>
public sealed class AppLifetime(PreferencesService preferences, AuthService auth, FocusSessionService focus, NavigationService navigation,
    DataService data, NotificationService notifications, ILogger<AppLifetime> logger)
{
    private const string TrayTipShownKey = "tray.tipShown";

    private MainWindow? _window;
    private TrayIcon? _tray;
    private bool _quitting;
    private int _lastTipMinute = -1;

    public bool IsWindowHidden { get; private set; }

    public void Attach(MainWindow window)
    {
        _window = window;
        window.AppWindow.Closing += OnClosing;
        preferences.Changed += (_, _) => UpdateTray();
        auth.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AuthService.User)) UpdateTray(); };
        focus.Tick += (_, _) => UpdateTip();
        focus.Ended += (_, _) => UpdateTip();
        UpdateTray();
    }

    private bool RunsInBackground => auth.IsSignedIn && preferences.Current.KeepRunningInTray;

    public void ShowMainWindow()
    {
        if (_window is null) return;
        _window.AppWindow.Show(true);
        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        _window.Activate();
        IsWindowHidden = false;
    }

    public void Quit()
    {
        logger.LogInformation("Quit requested");
        if (focus.IsActive && focus.Mode == FocusMode.Ultra)
        {
            ShowMainWindow();
            focus.ShouldBlockClose(); // explains that Ultra-Focus is locked
            return;
        }
        if (focus.IsActive) focus.ShouldBlockClose(); // ends a normal session and logs it
        _quitting = true;
        Shutdown();
        _window?.Close();
        Application.Current.Exit();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_quitting) return;
        if (focus.IsActive && focus.Mode == FocusMode.Ultra)
        {
            e.Cancel = focus.ShouldBlockClose();
            return;
        }
        if (RunsInBackground)
        {
            // Keep running (a normal Focus session carries on too); the tray icon brings the window back.
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (focus.ShouldBlockClose())
        {
            e.Cancel = true;
            return;
        }
        _quitting = true;
        Shutdown();
    }

    private async void HideToTray()
    {
        if (_window is null) return;
        UpdateTray();
        _window.AppWindow.Hide();
        IsWindowHidden = true;
        try
        {
            if (await data.GetMetaAsync(TrayTipShownKey) is not null) return;
            await data.SetMetaAsync(TrayTipShownKey, "1");
            notifications.Show("tray-tip", "Nerdy is still running",
                "Reminders keep coming while Nerdy is in the notification area. Right-click its icon to quit, or turn this off in Settings.",
                new Dictionary<string, string> { ["action"] = "open", ["section"] = NavSections.Settings });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Couldn't show the tray tip");
        }
    }

    private void UpdateTray()
    {
        if (RunsInBackground)
        {
            if (_tray is not null) return;
            try
            {
                _tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Nerdy.ico"), "Nerdy", ShowMainWindow,
                [
                    new TrayMenuItem("Open Nerdy", ShowMainWindow, IsDefault: true),
                    new TrayMenuItem("Start a focus session", () => { ShowMainWindow(); navigation.Navigate(NavSections.Focus); }),
                    null,
                    new TrayMenuItem("Quit Nerdy", Quit)
                ]);
                UpdateTip();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't create the notification-area icon");
            }
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
            if (IsWindowHidden) ShowMainWindow(); // never leave Nerdy running invisibly without an icon
        }
    }

    private void UpdateTip()
    {
        if (_tray is null) return;
        if (!focus.IsActive)
        {
            _lastTipMinute = -1;
            _tray.SetTip("Nerdy");
            return;
        }
        var minutes = (focus.RemainingSeconds + 59) / 60;
        if (minutes == _lastTipMinute) return;
        _lastTipMinute = minutes;
        _tray.SetTip($"Nerdy · {(focus.Mode == FocusMode.Ultra ? "Ultra-Focus" : "Focus")} · {Fmt.HoursMinutes(minutes)} left");
    }

    private void Shutdown()
    {
        _tray?.Dispose();
        _tray = null;
    }
}
