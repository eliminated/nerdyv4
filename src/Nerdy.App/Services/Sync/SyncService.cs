using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Net;
using Microsoft.UI.Dispatching;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Data;
using Nerdy.Contracts;
using Windows.Networking.Connectivity;

namespace Nerdy.App.Services.Sync;

public enum SyncState
{
    Idle, Syncing, Offline, Error,
    /// <summary>The server no longer accepts this version's format. Edits wait on this PC until Nerdy updates.</summary>
    UpdateRequired
}

/// <summary>
/// Keeps the local store and the server in step (protocol in docs/ARCHITECTURE.md): on start, ~2 s after local
/// edits, every 5 minutes, and when the network comes back. Only one sync runs at a time; edits made during a
/// sync trigger another pass. Runs on the UI thread (network calls are async, so the UI stays responsive).
/// </summary>
public sealed partial class SyncService : ObservableObject
{
    private const string VersionKey = "sync.version";
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ApiClient _api;
    private readonly AuthService _auth;
    private readonly DataService _data;
    private readonly ILogger<SyncService> _logger;
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _debounceTimer;
    private DispatcherQueueTimer? _intervalTimer;
    private bool _running;
    private bool _again;

    public SyncService(ApiClient api, AuthService auth, DataService data, ILogger<SyncService> logger)
    {
        _api = api;
        _auth = auth;
        _data = data;
        _logger = logger;
    }

    [ObservableProperty]
    public partial SyncState State { get; private set; } = SyncState.Idle;

    [ObservableProperty]
    public partial DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Raised on the UI thread after each successful sync.</summary>
    public event EventHandler? Synced;

    public void Start()
    {
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        _debounceTimer ??= CreateTimer(Debounce, repeating: false);
        _intervalTimer ??= CreateTimer(Interval, repeating: true);
        _data.LocalEdit -= OnLocalEdit;
        _data.LocalEdit += OnLocalEdit;
        NetworkInformation.NetworkStatusChanged -= OnNetworkChanged;
        NetworkInformation.NetworkStatusChanged += OnNetworkChanged;
        _intervalTimer.Start();
        _ = SyncNowAsync();
    }

    public void Stop()
    {
        _data.LocalEdit -= OnLocalEdit;
        NetworkInformation.NetworkStatusChanged -= OnNetworkChanged;
        _debounceTimer?.Stop();
        _intervalTimer?.Stop();
        State = SyncState.Idle;
    }

    public async Task SyncNowAsync()
    {
        if (!_auth.IsSignedIn || !_data.IsOpen) return;
        if (_running) { _again = true; return; }
        _running = true;
        try
        {
            do
            {
                _again = false;
                await SyncOnceAsync();
            } while (_again && State != SyncState.Offline);
        }
        finally
        {
            _running = false;
        }
    }

    private async Task SyncOnceAsync()
    {
        State = SyncState.Syncing;
        var userId = _data.UserId;
        try
        {
            var since = long.TryParse(await _data.GetMetaAsync(VersionKey), out var v) ? v : 0;
            var pushed = _data.CollectDirty();
            var response = await _api.SyncAsync(new SyncRequest { Protocol = SyncProtocol.Current, SinceVersion = since, Changes = pushed });
            if (_data.UserId != userId) return; // signed out / switched account while waiting
            if (response.Protocol < SyncProtocol.Current)
            {
                // A server that hasn't been updated yet would hand back rows in the old shape: keep the local copy as it is.
                _logger.LogWarning("Sync skipped: the server speaks sync protocol {Server}, this app {App}", response.Protocol, SyncProtocol.Current);
                State = SyncState.Error;
                return;
            }

            _data.ApplySync(pushed, response.Changes);
            await _data.SetMetaAsync(VersionKey, response.Version.ToString());
            LastSyncedAt = DateTimeOffset.Now;
            State = SyncState.Idle;
            Synced?.Invoke(this, EventArgs.Empty);
        }
        catch (OfflineException)
        {
            State = SyncState.Offline;
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.UpgradeRequired)
        {
            _logger.LogWarning("Sync refused until Nerdy updates: {Message}", ex.UserMessage);
            State = SyncState.UpdateRequired;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Sync rejected: {Status} {Message}", ex.Status, ex.UserMessage);
            State = SyncState.Error;
        }
    }

    private void OnLocalEdit(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnNetworkChanged(object? sender)
    {
        // Fires on a background thread.
        _dispatcher?.TryEnqueue(() =>
        {
            if (State == SyncState.Offline && NetworkInformation.GetInternetConnectionProfile() is not null) _ = SyncNowAsync();
        });
    }

    private DispatcherQueueTimer CreateTimer(TimeSpan interval, bool repeating)
    {
        var timer = _dispatcher!.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = repeating;
        timer.Tick += (_, _) => _ = SyncNowAsync();
        return timer;
    }
}
