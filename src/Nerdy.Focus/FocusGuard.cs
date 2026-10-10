using System.Diagnostics;
using Nerdy.Focus.Interop;

namespace Nerdy.Focus;

public enum BlockedAppAction
{
    /// <summary>Minimise the app's windows; they are restored when the session ends.</summary>
    Hide,
    /// <summary>Ask the app to close, then end it after a grace period (the user confirms this up front).</summary>
    Close
}

public enum FocusLockLevel
{
    /// <summary>Focus: non-allowed apps are hidden or closed.</summary>
    Standard,
    /// <summary>Ultra-Focus: additionally, Nerdy takes the foreground back whenever a non-allowed app grabs it.</summary>
    Ultra
}

public sealed class FocusGuardOptions
{
    public FocusLockLevel Level { get; init; } = FocusLockLevel.Standard;
    public BlockedAppAction BlockedAppAction { get; init; } = BlockedAppAction.Hide;
    /// <summary>Executable names such as "spotify.exe" (case-insensitive). Nerdy itself is always allowed.</summary>
    public IReadOnlyCollection<string> AllowedExeNames { get; init; } = [];
    /// <summary>Nerdy's main window, brought back to the front in Ultra-Focus.</summary>
    public nint OwnerWindow { get; init; }
    public TimeSpan CloseGracePeriod { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed record AppIdentity(string ExeName, string? ExePath, string DisplayName);

public sealed class AppBlockedEventArgs(AppIdentity app, BlockedAppAction action, bool succeeded) : EventArgs
{
    public AppIdentity App { get; } = app;
    public BlockedAppAction Action { get; } = action;
    /// <summary>False when Windows didn't let Nerdy act on the app (for example an app running as administrator).</summary>
    public bool Succeeded { get; } = succeeded;
}

/// <summary>
/// Keeps non-allowed apps out of the way for the duration of a focus session the user started.
/// It watches only which top-level window was shown or brought to the front (the standard accessibility
/// WinEvents) — it never observes input. Everything it hid is restored by <see cref="StopAsync"/>.
/// Events are raised on a background thread.
/// </summary>
public sealed class FocusGuard(Action<string>? log = null) : IAsyncDisposable
{
    private static readonly TimeSpan NotifyInterval = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly HashSet<nint> _hidden = [];
    private readonly HashSet<int> _closing = [];
    private readonly Dictionary<string, DateTime> _lastNotified = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<nint> _hooks = [];

    private FocusGuardOptions _options = new();
    private HashSet<string> _allowed = [];
    private NativeMethods.WinEventProc? _callback; // kept alive while hooks exist
    private Thread? _thread;
    private uint _threadId;

    public bool IsActive { get; private set; }

    public event EventHandler<AppBlockedEventArgs>? AppBlocked;

    public Task StartAsync(FocusGuardOptions options, CancellationToken ct = default)
    {
        if (IsActive) throw new InvalidOperationException("A focus session is already being guarded.");
        _options = options;
        _allowed = AppCatalog.Normalize(options.AllowedExeNames);
        lock (_lock) { _hidden.Clear(); _closing.Clear(); _lastNotified.Clear(); }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(started)) { IsBackground = true, Name = "Nerdy focus guard" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        IsActive = true;
        return started.Task.WaitAsync(ct);
    }

    public async Task StopAsync()
    {
        if (!IsActive) return;
        IsActive = false;
        if (_thread is { } thread)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
            await Task.Run(() => thread.Join(TimeSpan.FromSeconds(3)));
            _thread = null;
        }
        RestoreHidden(_ => true);
        log?.Invoke("Focus guard stopped; hidden windows restored.");
    }

    /// <summary>Changes the allow-list mid-session; windows of newly allowed apps are restored.</summary>
    public void UpdateAllowedApps(IReadOnlyCollection<string> exeNames)
    {
        var allowed = AppCatalog.Normalize(exeNames);
        _allowed = allowed;
        RestoreHidden(w => AppWindows.Describe(w) is { } d && allowed.Contains(d.Window.ExeName));
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ----- Guard thread -----

    private void Run(TaskCompletionSource started)
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _callback = OnWinEvent;
            foreach (var evt in new[] { NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_MINIMIZEEND, NativeMethods.EVENT_OBJECT_SHOW })
            {
                var hook = NativeMethods.SetWinEventHook(evt, evt, 0, _callback, 0, 0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
                if (hook != 0) _hooks.Add(hook);
            }

            // Deal with what is already open when the session starts.
            foreach (var (window, facts) in AppWindows.Snapshot()) Handle(window, facts, foreground: false);
            started.TrySetResult();
            log?.Invoke($"Focus guard started ({_options.Level}, {_options.BlockedAppAction}, {_allowed.Count} allowed apps).");

            while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(msg);
                NativeMethods.DispatchMessage(msg);
            }
        }
        catch (Exception ex)
        {
            started.TrySetException(ex);
            log?.Invoke($"Focus guard failed: {ex.Message}");
        }
        finally
        {
            foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
            _hooks.Clear();
            _callback = null;
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!IsActive || idObject != NativeMethods.OBJID_WINDOW || idChild != 0 || hwnd == 0) return;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return; // top-level windows only
        if (AppWindows.Describe(hwnd) is not { } d) return;
        Handle(d.Window, d.Facts, foreground: eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND);
    }

    private void Handle(AppWindow window, WindowFacts facts, bool foreground)
    {
        if (window.Handle == _options.OwnerWindow) return;
        if (WindowClassifier.Classify(facts, AppWindows.OwnProcessId, _allowed) != WindowVerdict.Block) return;

        if (_options.BlockedAppAction == BlockedAppAction.Close) Close(window);
        else Hide(window);

        if (foreground && _options.Level == FocusLockLevel.Ultra && _options.OwnerWindow != 0)
        {
            NativeMethods.BringWindowToTop(_options.OwnerWindow);
            NativeMethods.SetForegroundWindow(_options.OwnerWindow);
        }
    }

    private void Hide(AppWindow window)
    {
        if (!NativeMethods.IsIconic(window.Handle)) NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_MINIMIZE);
        // Windows refuses for apps running as administrator; report that instead of pretending.
        var ok = NativeMethods.IsIconic(window.Handle);
        if (ok) lock (_lock) _hidden.Add(window.Handle);
        Notify(window, BlockedAppAction.Hide, ok);
    }

    private void Close(AppWindow window)
    {
        lock (_lock)
        {
            if (!_closing.Add(window.ProcessId)) return;
        }
        Notify(window, BlockedAppAction.Close, succeeded: true);

        // Ask politely first (lets the app save state), then end it if it is still running after the grace period.
        foreach (var (w, _) in AppWindows.Snapshot().Where(x => x.Window.ProcessId == window.ProcessId))
            NativeMethods.PostMessage(w.Handle, NativeMethods.WM_CLOSE, 0, 0);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.CloseGracePeriod);
                using var process = Process.GetProcessById(window.ProcessId);
                if (!process.HasExited && process.SessionId == Process.GetCurrentProcess().SessionId &&
                    !WindowClassifier.IsSystemProcess(window.ExeName))
                {
                    process.Kill();
                    log?.Invoke($"Closed {window.ExeName} after the grace period.");
                }
            }
            catch (ArgumentException) { /* already exited */ }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Notify(window, BlockedAppAction.Close, succeeded: false, force: true);
            }
            finally
            {
                lock (_lock) _closing.Remove(window.ProcessId);
            }
        });
    }

    private void RestoreHidden(Func<nint, bool> which)
    {
        List<nint> restore;
        lock (_lock)
        {
            restore = _hidden.Where(which).ToList();
            foreach (var h in restore) _hidden.Remove(h);
        }
        foreach (var h in restore.Where(h => NativeMethods.IsWindow(h) && NativeMethods.IsIconic(h)))
            NativeMethods.ShowWindow(h, NativeMethods.SW_RESTORE);
    }

    private void Notify(AppWindow window, BlockedAppAction action, bool succeeded, bool force = false)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (!force && _lastNotified.TryGetValue(window.ExeName, out var last) && now - last < NotifyInterval) return;
            _lastNotified[window.ExeName] = now;
        }
        var app = new AppIdentity(window.ExeName, window.ExePath, AppWindows.DisplayName(window.ExePath, window.ExeName));
        AppBlocked?.Invoke(this, new AppBlockedEventArgs(app, action, succeeded));
    }
}
