using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Shell;
using Nerdy.App.Services.Sync;
using Velopack;
using Velopack.Sources;

namespace Nerdy.App.Services.Updates;

/// <summary>
/// Keeps an installed Nerdy up to date from the project's GitHub Releases (Velopack). It checks shortly after launch
/// and every few hours, downloads in the background, then offers a restart, never during a focus session. A
/// downloaded update that wasn't applied installs the next time Nerdy starts. Builds that weren't installed by the
/// installer (development builds, copied folders) never check.
/// </summary>
public sealed class UpdateService
{
    private const string Repository = "https://github.com/eliminated/nerdyv4";
    private static readonly TimeSpan FirstCheckAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private readonly UpdateManager _manager = new(new GithubSource(Repository, null, false));
    private readonly ToastService _toasts;
    private readonly FocusSessionService _focus;
    private readonly AppLifetime _lifetime;
    private readonly SyncService _sync;
    private readonly ILogger<UpdateService> _logger;
    private VelopackAsset? _ready;
    private bool _checking;

    public UpdateService(ToastService toasts, FocusSessionService focus, AppLifetime lifetime, SyncService sync, ILogger<UpdateService> logger)
    {
        _toasts = toasts;
        _focus = focus;
        _lifetime = lifetime;
        _sync = sync;
        _logger = logger;
        _focus.PropertyChanged += OnFocusChanged;
        _sync.PropertyChanged += OnSyncChanged;
    }

    /// <summary>Runs for the life of the app. Call once from the UI thread.</summary>
    public async Task RunAsync()
    {
        if (!_manager.IsInstalled) return;
        await Task.Delay(FirstCheckAfter);
        while (true)
        {
            await CheckAsync();
            await Task.Delay(CheckEvery);
        }
    }

    private async Task CheckAsync()
    {
        if (_checking || !_manager.IsInstalled) return;
        _checking = true;
        try
        {
            if (_ready is null)
            {
                var update = await _manager.CheckForUpdatesAsync();
                if (update is null) return;
                await _manager.DownloadUpdatesAsync(update);
                _ready = update.TargetFullRelease;
                _logger.LogInformation("Downloaded Nerdy {Version}; it installs on restart", _ready.Version);
            }
            OfferRestart();
        }
        catch (Exception ex)
        {
            // Offline, GitHub's rate limit, a full disk: try again at the next check.
            _logger.LogInformation("Update check failed: {Message}", ex.Message);
        }
        finally
        {
            _checking = false;
        }
    }

    private void OfferRestart()
    {
        if (_ready is not { } release || _focus.IsActive) return;
        _toasts.Show($"Nerdy {release.Version} is ready", "Restart", () => Restart(release));
    }

    private void Restart(VelopackAsset release)
    {
        if (_focus.IsActive) return;
        _logger.LogInformation("Restarting to install Nerdy {Version}", release.Version);
        // The updater waits for Nerdy to quit normally (saving everything), installs the update, then starts it again.
        _manager.WaitExitThenApplyUpdates(release, silent: true, restart: true);
        _lifetime.Quit();
    }

    // The server stopped accepting this version: look for the update now rather than at the next 6-hourly check.
    private void OnSyncChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncService.State) && _sync.State == SyncState.UpdateRequired) _ = CheckAsync();
    }

    // A session that ends is a good moment to offer the restart that was held back.
    private void OnFocusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusSessionService.IsActive) && !_focus.IsActive) OfferRestart();
    }
}
