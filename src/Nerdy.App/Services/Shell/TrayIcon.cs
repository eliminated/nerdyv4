using System.Runtime.InteropServices;

namespace Nerdy.App.Services.Shell;

/// <summary>An item in the tray icon's right-click menu; <c>null</c> in the list draws a separator.</summary>
public sealed record TrayMenuItem(string Label, Action Invoke, bool IsDefault = false);

/// <summary>
/// A notification-area icon (Shell_NotifyIcon) with a click action and a right-click menu. Owns a hidden window on
/// the UI thread that receives the icon's messages, and re-adds the icon if Explorer restarts. One instance at a time.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint WmNull = 0x0000;
    private const uint WmTray = 0x8000 + 1; // WM_APP + 1
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmCommand = 0x0111;
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifMessage = 0x01, NifIcon = 0x02, NifTip = 0x04, NifShowTip = 0x80;
    private const uint MfString = 0x0000, MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002, TpmBottomAlign = 0x0020;
    private const uint ImageIcon = 1, LrLoadFromFile = 0x10;
    private const int SmCxSmIcon = 49, SmCySmIcon = 50;
    private const string ClassName = "NerdyTrayWindow";

    private static TrayIcon? s_current;

    private readonly nint _hwnd;
    private readonly nint _icon;
    private readonly uint _taskbarCreated;
    private readonly Action _click;
    private readonly IReadOnlyList<TrayMenuItem?> _menu;
    private string _tip;
    private bool _disposed;

    public TrayIcon(string iconPath, string tip, Action click, IReadOnlyList<TrayMenuItem?> menu)
    {
        if (s_current is not null) throw new InvalidOperationException("Only one tray icon is supported.");
        _tip = tip;
        _click = click;
        _menu = menu;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _icon = LoadImageW(0, iconPath, ImageIcon, GetSystemMetrics(SmCxSmIcon), GetSystemMetrics(SmCySmIcon), LrLoadFromFile);

        var instance = GetModuleHandleW(null);
        fixed (char* className = ClassName)
        {
            var wc = new WndClassEx
            {
                cbSize = (uint)sizeof(WndClassEx),
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = instance,
                lpszClassName = className
            };
            RegisterClassExW(&wc); // fails harmlessly if already registered by an earlier instance
            _hwnd = CreateWindowExW(0x80 /* WS_EX_TOOLWINDOW */, className, className, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        }
        s_current = this;
        Add();
    }

    /// <summary>Changes the hover text (e.g. the time left in a focus session).</summary>
    public void SetTip(string tip)
    {
        if (_disposed || tip == _tip) return;
        _tip = tip;
        var data = Data(NifTip | NifShowTip);
        Shell_NotifyIconW(NimModify, &data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = Data(0);
        Shell_NotifyIconW(NimDelete, &data);
        if (_hwnd != 0) DestroyWindow(_hwnd);
        if (_icon != 0) DestroyIcon(_icon);
        s_current = null;
    }

    private void Add()
    {
        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        Shell_NotifyIconW(NimAdd, &data);
    }

    private NotifyIconData Data(uint flags)
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)sizeof(NotifyIconData),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WmTray,
            hIcon = _icon
        };
        var tip = _tip.Length > 127 ? _tip[..127] : _tip;
        for (var i = 0; i < tip.Length; i++) data.szTip[i] = tip[i];
        return data;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < _menu.Count; i++)
            {
                if (_menu[i] is { } item)
                {
                    AppendMenuW(menu, MfString, (nuint)(i + 1), item.Label);
                    if (item.IsDefault) SetMenuDefaultItem(menu, (uint)(i + 1), 0);
                }
                else AppendMenuW(menu, MfSeparator, 0, null);
            }
            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd); // so the menu closes when clicking elsewhere
            // The choice arrives as WM_COMMAND (also how keyboard users and screen readers pick an item).
            TrackPopupMenuEx(menu, TpmRightButton | TpmBottomAlign, point.X, point.Y, _hwnd, 0);
            PostMessageW(_hwnd, WmNull, 0, 0);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WmCommand)
        {
            var id = (int)(wParam & 0xFFFF);
            if (id >= 1 && id <= _menu.Count && _menu[id - 1] is { } chosen) chosen.Invoke();
        }
        else if (message == WmTray)
        {
            switch ((uint)(lParam & 0xFFFF))
            {
                case WmLButtonUp: _click(); break;
                case WmRButtonUp: ShowMenu(); break;
            }
        }
        else if (message == _taskbarCreated)
        {
            Add(); // Explorer restarted
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (s_current is { } tray && tray._hwnd == hwnd) tray.OnMessage(message, wParam, lParam);
        }
        catch
        {
            // Never let an exception cross into native code.
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    // ----- Interop -----

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WndClassEx* wc);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}
