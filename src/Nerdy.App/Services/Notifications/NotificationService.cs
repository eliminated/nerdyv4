using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Nerdy.App.Services.Notifications;

public sealed record NotificationButton(string Label, IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// Windows toast notifications through the built-in Windows.UI.Notifications API. (The Windows App SDK's
/// AppNotificationManager needs the separately installed Windows App Runtime, which this self-contained app doesn't
/// use.) Nerdy registers its app id per user under HKCU\Software\Classes\AppUserModelId so toasts show its name and
/// icon. Clicks are handled in-process via <see cref="Invoked"/> on the UI thread; Nerdy is running whenever it sends
/// one (it stays in the notification area). When toasts aren't available, <see cref="IsAvailable"/> is false and
/// callers fall back to in-app toasts.
/// </summary>
public sealed class NotificationService(ILogger<NotificationService> logger)
{
    public const string AppUserModelId = "Nerdy.Desktop";
    private const string Group = "nerdy";

    private readonly Dictionary<string, ToastNotification> _shown = [];
    private DispatcherQueue? _dispatcher;
    private ToastNotifier? _notifier;

    public bool IsAvailable { get; private set; }

    public event EventHandler<IReadOnlyDictionary<string, string>>? Invoked;

    /// <summary>Registers Nerdy's app id and creates the notifier. Call once on the UI thread.</summary>
    public void Initialize()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        try
        {
            // A test build with its own data folder leaves the registration (and its icon path) to the installed Nerdy.
            if (!AppPaths.IsSeparate)
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}");
                key.SetValue("DisplayName", "Nerdy");
                key.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "NerdyLogo64.png"));
            }
            _notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Windows notifications unavailable; using in-app toasts.");
        }
    }

    /// <param name="tag">Identifies the notification so a newer one replaces it (and it can be removed).</param>
    /// <param name="persistent">Stays on screen until acted on (reminder scenario); needs at least one button.</param>
    public void Show(string tag, string title, string body, IReadOnlyDictionary<string, string> clickArguments,
        IReadOnlyList<NotificationButton>? buttons = null, bool persistent = false)
    {
        if (_notifier is null) return;
        tag = TagFor(tag);
        try
        {
            var xml = new StringBuilder();
            xml.Append("<toast launch=\"").Append(Escape(Encode(clickArguments))).Append('"');
            if (persistent && buttons is { Count: > 0 }) xml.Append(" scenario=\"reminder\"");
            xml.Append("><visual><binding template=\"ToastGeneric\"><text>").Append(Escape(title)).Append("</text>");
            if (body.Length > 0) xml.Append("<text>").Append(Escape(body)).Append("</text>");
            xml.Append("</binding></visual>");
            if (buttons is { Count: > 0 })
            {
                xml.Append("<actions>");
                foreach (var b in buttons)
                    xml.Append("<action activationType=\"foreground\" content=\"").Append(Escape(b.Label))
                       .Append("\" arguments=\"").Append(Escape(Encode(b.Arguments))).Append("\"/>");
                xml.Append("</actions>");
            }
            xml.Append("</toast>");

            var document = new XmlDocument();
            document.LoadXml(xml.ToString());
            var toast = new ToastNotification(document) { Tag = tag, Group = Group };
            toast.Activated += (_, args) =>
            {
                if (args is ToastActivatedEventArgs activated) Raise(Decode(activated.Arguments));
            };
            toast.Dismissed += (_, _) => Forget(tag, toast);
            toast.Failed += (_, e) => logger.LogWarning(e.ErrorCode, "A notification failed to show");
            _shown[tag] = toast; // keep it (and its Activated handler) alive while it can be clicked
            _notifier.Show(toast);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't show a notification");
        }
    }

    public void Remove(string tag)
    {
        if (!IsAvailable) return;
        try { ToastNotificationManager.History.Remove(TagFor(tag), Group, AppUserModelId); }
        catch (Exception ex) { logger.LogDebug(ex, "Couldn't remove notification {Tag}", tag); }
    }

    /// <summary>Windows caps tags at 16 characters (64 on newer builds), so longer ones are hashed to fit.</summary>
    private static string TagFor(string tag) =>
        tag.Length <= 16 ? tag : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag)))[..16];

    private void Forget(string tag, ToastNotification toast)
    {
        void Drop() { if (_shown.TryGetValue(tag, out var current) && current == toast) _shown.Remove(tag); }
        if (_dispatcher is null || _dispatcher.HasThreadAccess) Drop();
        else _dispatcher.TryEnqueue(Drop);
    }

    private void Raise(IReadOnlyDictionary<string, string> arguments)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) Invoked?.Invoke(this, arguments);
        else _dispatcher.TryEnqueue(() => Invoked?.Invoke(this, arguments));
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";

    private static string Encode(IReadOnlyDictionary<string, string> arguments) =>
        string.Join('&', arguments.Select(a => $"{Uri.EscapeDataString(a.Key)}={Uri.EscapeDataString(a.Value)}"));

    private static Dictionary<string, string> Decode(string arguments) =>
        arguments.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1]));
}
