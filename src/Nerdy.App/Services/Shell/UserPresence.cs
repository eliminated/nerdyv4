using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nerdy.App.Services.Shell;

/// <summary>Whether someone seems to be at the PC: it isn't locked and the keyboard or mouse was used recently.</summary>
internal static partial class UserPresence
{
    public static bool IsAway(TimeSpan idleAfter) => IsLocked() || IdleTime() >= idleAfter;

    /// <summary>LogonUI runs while the lock screen (or the Ctrl+Alt+Del screen) is showing.</summary>
    private static bool IsLocked()
    {
        var processes = Process.GetProcessesByName("LogonUI");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);
}
