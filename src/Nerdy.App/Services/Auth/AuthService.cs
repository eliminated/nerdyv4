using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Nerdy.App.Services.Api;
using Nerdy.Contracts;

namespace Nerdy.App.Services.Auth;

/// <summary>
/// Owns the signed-in session: email/password and Microsoft/Google sign-in, token refresh (single-flight),
/// offline restore of the last session, and sign-out. Use from the UI thread.
/// </summary>
public sealed partial class AuthService : ObservableObject, ISessionTokenSource
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly ApiClient _api;
    private readonly TokenStore _store;
    private readonly ILogger<AuthService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private StoredSession? _session;

    public AuthService(ApiClient api, TokenStore store, ILogger<AuthService> logger)
    {
        _api = api;
        _store = store;
        _logger = logger;
        api.Tokens = this;
    }

    [ObservableProperty]
    public partial UserDto? User { get; private set; }

    public bool IsSignedIn => User is not null;

    /// <summary>Raised when the session ends: the user signed out or the server no longer accepts it.</summary>
    public event EventHandler<bool>? SignedOut; // arg: true when it expired rather than being a deliberate sign-out

    /// <summary>Restores the last session from disk. Works offline; tokens are refreshed when the API is reachable.</summary>
    public bool TryRestore()
    {
        _session = _store.Load();
        if (_session is null || _session.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            _session = null;
            _store.Clear();
            return false;
        }
        User = _session.User;
        OnPropertyChanged(nameof(IsSignedIn));
        return true;
    }

    public async Task SignInAsync(string email, string password, CancellationToken ct = default) =>
        Accept(await _api.LoginAsync(new LoginRequest { Email = email.Trim(), Password = password }, ct));

    public async Task RegisterAsync(string displayName, string email, string password, CancellationToken ct = default) =>
        Accept(await _api.RegisterAsync(new RegisterRequest { DisplayName = displayName.Trim(), Email = email.Trim(), Password = password }, ct));

    public async Task SignInWithProviderAsync(string provider, CancellationToken ct = default)
    {
        var result = await LoopbackSignIn.RunAsync((redirect, state, challenge) => _api.ExternalStartUri(provider, redirect, state, challenge), ct);
        Accept(await _api.ExchangeExternalCodeAsync(new ExternalExchangeRequest
        {
            Code = result.Code,
            CodeVerifier = result.CodeVerifier,
            RedirectUri = result.RedirectUri
        }, ct));
    }

    public Task<List<string>> GetProvidersAsync(CancellationToken ct = default) => _api.GetProvidersAsync(ct);

    public Task ForgotPasswordAsync(string email, CancellationToken ct = default) =>
        _api.ForgotPasswordAsync(new ForgotPasswordRequest { Email = email.Trim() }, ct);

    public Task ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct = default) =>
        _api.ResetPasswordAsync(new ResetPasswordRequest { Email = email.Trim(), Token = token.Trim(), NewPassword = newPassword }, ct);

    /// <summary>Refreshes the cached profile from the server (ignored when offline).</summary>
    public async Task RefreshUserAsync(CancellationToken ct = default)
    {
        try { UpdateUser(await _api.GetMeAsync(ct)); }
        catch (OfflineException) { }
    }

    public async Task UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default) =>
        UpdateUser(await _api.UpdateMeAsync(request, ct));

    public async Task DeleteAccountAsync(CancellationToken ct = default)
    {
        await _api.DeleteMeAsync(ct);
        EndSession(expired: false);
    }

    public async Task SignOutAsync()
    {
        if (_session is { } s)
        {
            try { await _api.LogoutAsync(new RefreshRequest { RefreshToken = s.RefreshToken }); }
            catch (Exception ex) when (ex is OfflineException or ApiException)
            {
                _logger.LogInformation("Couldn't revoke the refresh token on sign-out: {Message}", ex.Message);
            }
        }
        EndSession(expired: false);
    }

    async Task<string?> ISessionTokenSource.GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        var session = _session;
        if (session is null) return null;
        if (!forceRefresh && session.AccessTokenExpiresAt - RefreshMargin > DateTimeOffset.UtcNow) return session.AccessToken;

        await _refreshGate.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while we waited.
            if (_session is null) return null;
            if (!ReferenceEquals(_session, session) && _session.AccessTokenExpiresAt - RefreshMargin > DateTimeOffset.UtcNow)
                return _session.AccessToken;

            try
            {
                var response = await _api.RefreshAsync(new RefreshRequest { RefreshToken = _session.RefreshToken }, ct);
                Accept(response);
                return response.AccessToken;
            }
            catch (ApiException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            {
                _logger.LogInformation("Session refresh rejected ({Status}); signing out.", ex.Status);
                EndSession(expired: true);
                return null;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void Accept(AuthResponse response)
    {
        _session = StoredSession.From(response);
        _store.Save(_session);
        User = response.User;
        OnPropertyChanged(nameof(IsSignedIn));
    }

    private void UpdateUser(UserDto user)
    {
        if (_session is null) return;
        _session = _session with { User = user };
        _store.Save(_session);
        User = user;
    }

    private void EndSession(bool expired)
    {
        _session = null;
        _store.Clear();
        User = null;
        OnPropertyChanged(nameof(IsSignedIn));
        SignedOut?.Invoke(this, expired);
    }
}
