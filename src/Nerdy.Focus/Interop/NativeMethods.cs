using System.Runtime.InteropServices;

namespace Nerdy.Focus.Interop;

/// <summary>Win32 window APIs used to find and hide/close app windows. No input APIs are used.</summary>
internal static partial class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const int DWMWA_CLOAKED = 14;
    public const uint GW_OWNER = 4;

    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClassName(nint hwnd, [Out] char[] className, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(nint hwnd, [Out] char[] text, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint command);

    public const uint GA_ROOT = 2;

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc callback, uint idProcess, uint idThread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out MSG msg, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, [Out] char[] exeName, ref int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    public static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var n = GetClassName(hwnd, buffer, buffer.Length);
        return n > 0 ? new string(buffer, 0, n) : "";
    }

    public static string Title(nint hwnd)
    {
        var buffer = new char[512];
        var n = GetWindowText(hwnd, buffer, buffer.Length);
        return n > 0 ? new string(buffer, 0, n) : "";
    }

    public static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>Full path of a process's executable, or null when it can't be opened (e.g. elevated or protected).</summary>
    public static string? ProcessPath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageName(h, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }
}
