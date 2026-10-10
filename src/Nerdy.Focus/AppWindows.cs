using System.Diagnostics;
using Nerdy.Focus.Interop;

namespace Nerdy.Focus;

/// <summary>A top-level window together with the app it belongs to.</summary>
internal sealed record AppWindow(nint Handle, int ProcessId, string ExeName, string ExePath, string ClassName, string Title)
{
    public WindowFacts Facts(bool visible, bool cloaked, bool toolWindow, bool hasOwner) =>
        new(ExeName, ClassName, visible, cloaked, toolWindow, hasOwner, ProcessId);
}

/// <summary>Reads the current top-level windows (read-only).</summary>
internal static class AppWindows
{
    public static readonly int OwnProcessId = Environment.ProcessId;

    public static List<(AppWindow Window, WindowFacts Facts)> Snapshot()
    {
        var result = new List<(AppWindow, WindowFacts)>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (Describe(hwnd) is { } d) result.Add(d);
            return true;
        }, 0);
        return result;
    }

    public static (AppWindow Window, WindowFacts Facts)? Describe(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var className = NativeMethods.ClassName(hwnd);
        var path = NativeMethods.ProcessPath(pid);

        // UWP apps (Calculator, Settings…) live inside ApplicationFrameHost; the real app owns a child CoreWindow.
        if (path is not null && Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var hostPid = pid;
            NativeMethods.EnumChildWindows(hwnd, (child, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(child, out var childPid);
                if (childPid == hostPid) return true;
                pid = childPid;
                path = NativeMethods.ProcessPath(childPid);
                return false;
            }, 0);
        }

        var exePath = path ?? "";
        var exeName = path is null ? "" : Path.GetFileName(path).ToLowerInvariant();
        var exStyle = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var window = new AppWindow(hwnd, (int)pid, exeName, exePath, className, NativeMethods.Title(hwnd));
        var facts = window.Facts(
            visible: NativeMethods.IsWindowVisible(hwnd),
            cloaked: NativeMethods.IsCloaked(hwnd),
            toolWindow: (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0,
            hasOwner: NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != 0);
        return (window, facts);
    }

    /// <summary>The friendly name of an executable ("Spotify") from its version info, falling back to the file name.</summary>
    public static string DisplayName(string exePath, string exeName)
    {
        if (exeName == WindowClassifier.FileExplorerExe) return "File Explorer";
        try
        {
            if (File.Exists(exePath))
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName.Trim();
            }
        }
        catch (IOException) { /* fall through */ }
        var name = Path.GetFileNameWithoutExtension(exeName);
        return name.Length == 0 ? exeName : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
