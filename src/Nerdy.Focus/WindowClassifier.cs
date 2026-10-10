namespace Nerdy.Focus;

/// <summary>What Nerdy knows about a top-level window when deciding whether it belongs to a blockable app.</summary>
internal readonly record struct WindowFacts(
    string ExeName,
    string ClassName,
    bool Visible,
    bool Cloaked,
    bool ToolWindow,
    bool HasOwner,
    int ProcessId);

internal enum WindowVerdict
{
    /// <summary>Not an app window (shell, system, tool/owned window, invisible) — never touched.</summary>
    Ignore,
    /// <summary>An app window the user allowed.</summary>
    Allowed,
    /// <summary>An app window that isn't allowed during the session.</summary>
    Block
}

/// <summary>
/// Decides which windows count as "apps" for focus sessions. Shell surfaces, input hosts, security UI and Task
/// Manager (a deliberate way out) are never touched, nor is Nerdy itself.
/// </summary>
internal static class WindowClassifier
{
    /// <summary>Processes whose windows are part of Windows itself.</summary>
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm.exe", "csrss.exe", "winlogon.exe", "logonui.exe", "consent.exe", "lockapp.exe",
        "shellexperiencehost.exe", "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe",
        "textinputhost.exe", "ctfmon.exe", "systemsettingsbroker.exe", "taskmgr.exe", "sihost.exe",
        "applicationframehost.exe", "credentialuibroker.exe", "useroobebroker.exe", "widgets.exe",
        "securityhealthsystray.exe", "rundll32.exe", "openwith.exe", "pickerhost.exe"
    };

    /// <summary>explorer.exe windows that are the desktop or taskbar rather than File Explorer.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
        "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd"
    };

    public const string FileExplorerExe = "explorer.exe";
    public const string FileExplorerClass = "CabinetWClass";

    public static WindowVerdict Classify(in WindowFacts w, int ownProcessId, IReadOnlySet<string> allowedExeNames)
    {
        if (w.ProcessId == ownProcessId) return WindowVerdict.Ignore;
        if (!w.Visible || w.Cloaked || w.ToolWindow || w.HasOwner) return WindowVerdict.Ignore;
        if (string.IsNullOrEmpty(w.ExeName)) return WindowVerdict.Ignore; // couldn't identify it (elevated/protected)
        if (SystemProcesses.Contains(w.ExeName) || ShellClasses.Contains(w.ClassName)) return WindowVerdict.Ignore;
        if (w.ExeName.Equals(FileExplorerExe, StringComparison.OrdinalIgnoreCase) && w.ClassName != FileExplorerClass)
            return WindowVerdict.Ignore;

        return allowedExeNames.Contains(w.ExeName) ? WindowVerdict.Allowed : WindowVerdict.Block;
    }

    public static bool IsSystemProcess(string exeName) => SystemProcesses.Contains(exeName);
}
