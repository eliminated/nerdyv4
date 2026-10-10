using Microsoft.Extensions.Logging;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Classes;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Sync;

namespace Nerdy.App.Services;

/// <summary>
/// Owns the signed-in lifecycle: restores the session on launch, opens the user's local data, starts sync and
/// background services, routes to onboarding or Home, and tears everything down on sign-out.
/// </summary>
public sealed class SessionController(
    AuthService auth,
    DataService data,
    SyncService sync,
    ClassesService classes,
    PreferencesService preferences,
    NavigationService navigation,
    Focus.FocusSessionService focus,
    ToastService toasts,
    ILogger<SessionController> logger)
{
    public const string OnboardingDoneKey = "onboarding.done";

    private bool _started;

    /// <summary>Raised after the user's data is open (on sign-in or launch).</summary>
    public event EventHandler? SessionStarted;

    /// <summary>Raised before the user's data is closed (on sign-out or session expiry).</summary>
    public event EventHandler? SessionEnding;

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        auth.SignedOut += OnSignedOut;

        if (auth.TryRestore()) await EnterAsync();
        else ShowSignIn(expired: false);
    }

    /// <summary>Called after a successful sign-in or registration.</summary>
    public async Task EnterAsync()
    {
        if (auth.User is not { } user) return;
        logger.LogInformation("Opening session for {UserId}", user.Id);

        await data.OpenAsync(user.Id);
        await classes.LoadCacheAsync();
        await focus.RecoverInterruptedAsync();
        sync.Start();
        SessionStarted?.Invoke(this, EventArgs.Empty);

        var onboarded = user.OnboardingCompleted || await data.GetMetaAsync(OnboardingDoneKey) == "1";
        navigation.ShowRoot(onboarded ? RootView.Shell : RootView.Onboarding);
        if (onboarded) navigation.Navigate(Models.NavSections.Home);

        _ = RefreshInBackgroundAsync();
    }

    public async Task CompleteOnboardingAsync()
    {
        await data.SetMetaAsync(OnboardingDoneKey, "1");
        try { await auth.UpdateProfileAsync(new Contracts.UpdateProfileRequest { OnboardingCompleted = true }); }
        catch (Exception ex) when (ex is Api.OfflineException or Api.ApiException)
        {
            logger.LogInformation("Onboarding flag will be sent later: {Message}", ex.Message);
        }
        navigation.ShowRoot(RootView.Shell);
        navigation.Navigate(Models.NavSections.Home);
    }

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            await auth.RefreshUserAsync();
            await classes.RefreshAsync();
        }
        catch (Exception ex)
        {
            logger.LogInformation("Background refresh failed: {Message}", ex.Message);
        }
    }

    private async void OnSignedOut(object? sender, bool expired)
    {
        SessionEnding?.Invoke(this, EventArgs.Empty);
        sync.Stop();
        classes.Clear();
        await data.CloseAsync();
        ShowSignIn(expired);
        if (expired) toasts.Show("Your session expired. Please sign in again.", ToastKind.Warning);
    }

    private void ShowSignIn(bool expired)
    {
        preferences.ApplyDefaults();
        navigation.ShowRoot(RootView.Auth, expired);
    }
}
