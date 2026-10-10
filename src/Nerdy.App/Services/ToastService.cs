namespace Nerdy.App.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed record Toast(string Message, ToastKind Kind, string? ActionLabel = null, Action? Action = null);

/// <summary>Short in-app confirmations ("Reminder added"), shown as a pill at the bottom of the window.</summary>
public sealed class ToastService
{
    public event EventHandler<Toast>? Requested;

    public void Show(string message, ToastKind kind = ToastKind.Info) => Requested?.Invoke(this, new Toast(message, kind));

    public void Show(string message, string actionLabel, Action action, ToastKind kind = ToastKind.Info) =>
        Requested?.Invoke(this, new Toast(message, kind, actionLabel, action));

    /// <summary>Shows a friendly message for a failed operation.</summary>
    public void Error(Exception ex) => Show(ex switch
    {
        Api.OfflineException => "You're offline. Try again when you're connected.",
        Api.ApiException api => api.UserMessage,
        _ => ex.Message
    }, ToastKind.Error);
}
