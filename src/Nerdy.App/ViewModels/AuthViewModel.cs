using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nerdy.App.Services;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Auth;
using Nerdy.Contracts;

namespace Nerdy.App.ViewModels;

public enum AuthMode { SignIn, Register, Forgot, Reset }

public sealed partial class AuthViewModel(AuthService auth, SessionController session) : ObservableObject, INavigationAware
{
    private CancellationTokenSource? _externalCts;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(SubmitLabel), nameof(IsSignIn), nameof(IsRegister), nameof(IsForgot), nameof(IsReset), nameof(IsForgotOrReset), nameof(ShowPassword), nameof(ShowExternal), nameof(SwitchPrompt), nameof(SwitchAction))]
    public partial AuthMode Mode { get; set; } = AuthMode.SignIn;

    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string DisplayName { get; set; } = "";
    [ObservableProperty] public partial string ResetToken { get; set; } = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasInfo))]
    public partial string? InfoMessage { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowExternal))]
    public partial bool HasMicrosoft { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowExternal))]
    public partial bool HasGoogle { get; set; }

    [ObservableProperty]
    public partial bool IsWaitingForBrowser { get; set; }

    public bool CanInteract => !IsBusy;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);
    public bool IsSignIn => Mode == AuthMode.SignIn;
    public bool IsRegister => Mode == AuthMode.Register;
    public bool IsForgot => Mode == AuthMode.Forgot;
    public bool IsReset => Mode == AuthMode.Reset;
    public bool IsForgotOrReset => Mode is AuthMode.Forgot or AuthMode.Reset;
    public bool ShowPassword => Mode is AuthMode.SignIn or AuthMode.Register or AuthMode.Reset;
    public bool ShowExternal => Mode is AuthMode.SignIn or AuthMode.Register && (HasMicrosoft || HasGoogle);

    public string Title => Mode switch
    {
        AuthMode.Register => "Create your account",
        AuthMode.Forgot => "Reset your password",
        AuthMode.Reset => "Choose a new password",
        _ => "Welcome back"
    };

    public string Subtitle => Mode switch
    {
        AuthMode.Register => "Plan your studies, protect your focus, and see your progress.",
        AuthMode.Forgot => "We'll email you a reset code.",
        AuthMode.Reset => "Paste the code from the email and pick a new password.",
        _ => "Sign in to pick up where you left off."
    };

    public string SubmitLabel => Mode switch
    {
        AuthMode.Register => "Create account",
        AuthMode.Forgot => "Send reset code",
        AuthMode.Reset => "Reset password",
        _ => "Sign in"
    };

    public string SwitchPrompt => Mode == AuthMode.Register ? "Already have an account?" : "New to Nerdy?";
    public string SwitchAction => Mode == AuthMode.Register ? "Sign in" : "Create an account";

    public async void OnNavigatedTo(object? parameter)
    {
        if (parameter is true) InfoMessage = "Your session expired. Please sign in again.";
        try
        {
            var providers = await auth.GetProvidersAsync();
            HasMicrosoft = providers.Contains(AuthProviders.Microsoft, StringComparer.OrdinalIgnoreCase);
            HasGoogle = providers.Contains(AuthProviders.Google, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is OfflineException or ApiException)
        {
            ErrorMessage = "Nerdy can't reach its server right now. Check your connection and try again.";
        }
    }

    public void OnNavigatedFrom() => _externalCts?.Cancel();

    [RelayCommand]
    private void ToggleRegister()
    {
        Mode = Mode == AuthMode.Register ? AuthMode.SignIn : AuthMode.Register;
        Clear();
    }

    [RelayCommand]
    private void ShowForgot()
    {
        Mode = AuthMode.Forgot;
        Clear();
    }

    [RelayCommand]
    private void BackToSignIn()
    {
        Mode = AuthMode.SignIn;
        Clear();
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = Validate();
        if (ErrorMessage is not null) return;

        await RunAsync(async () =>
        {
            switch (Mode)
            {
                case AuthMode.SignIn:
                    await auth.SignInAsync(Email, Password);
                    await session.EnterAsync();
                    break;
                case AuthMode.Register:
                    await auth.RegisterAsync(DisplayName, Email, Password);
                    await session.EnterAsync();
                    break;
                case AuthMode.Forgot:
                    await auth.ForgotPasswordAsync(Email);
                    Mode = AuthMode.Reset;
                    InfoMessage = $"If an account exists for {Email.Trim()}, a reset code is on its way.";
                    break;
                case AuthMode.Reset:
                    await auth.ResetPasswordAsync(Email, ResetToken, Password);
                    Mode = AuthMode.SignIn;
                    Password = "";
                    InfoMessage = "Password changed. Sign in with your new password.";
                    break;
            }
        });
    }

    [RelayCommand]
    private Task SignInWithMicrosoftAsync() => ExternalAsync(AuthProviders.Microsoft);

    [RelayCommand]
    private Task SignInWithGoogleAsync() => ExternalAsync(AuthProviders.Google);

    [RelayCommand]
    private void CancelExternal() => _externalCts?.Cancel();

    private async Task ExternalAsync(string provider)
    {
        _externalCts?.Cancel();
        _externalCts = new CancellationTokenSource();
        var ct = _externalCts.Token;
        IsWaitingForBrowser = true;
        try
        {
            await RunAsync(async () =>
            {
                await auth.SignInWithProviderAsync(provider, ct);
                await session.EnterAsync();
            });
        }
        finally
        {
            IsWaitingForBrowser = false;
        }
    }

    private async Task RunAsync(Func<Task> work)
    {
        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // The user cancelled the browser sign-in.
        }
        catch (OfflineException)
        {
            ErrorMessage = "Nerdy can't reach its server right now. Check your connection and try again.";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Status == System.Net.HttpStatusCode.Unauthorized ? "That email and password don't match." : ex.UserMessage;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? Validate()
    {
        if (!new EmailAddressAttribute().IsValid(Email.Trim()) || string.IsNullOrWhiteSpace(Email)) return "Enter a valid email address.";
        if (Mode == AuthMode.Register && string.IsNullOrWhiteSpace(DisplayName)) return "Tell us what to call you.";
        if (Mode is AuthMode.Register or AuthMode.Reset)
        {
            if (Password.Length < 8 || !Password.Any(char.IsDigit)) return "Use at least 8 characters, including a number.";
        }
        if (Mode == AuthMode.SignIn && Password.Length == 0) return "Enter your password.";
        if (Mode == AuthMode.Reset && string.IsNullOrWhiteSpace(ResetToken)) return "Paste the reset code from the email.";
        return null;
    }

    private void Clear()
    {
        ErrorMessage = null;
        InfoMessage = null;
        Password = "";
    }
}
