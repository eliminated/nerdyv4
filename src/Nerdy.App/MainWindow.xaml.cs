using System.Runtime.InteropServices;
using FluentIcons.Common;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Nerdy.App.Services;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Theming;
using Windows.Graphics;

namespace Nerdy.App;

/// <summary>Pages shown in the root frame provide their own title bar (drag region) element.</summary>
public interface ITitleBarHost
{
    UIElement? TitleBarElement { get; }
}

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimerHolder _toastTimer;
    private Action? _toastAction;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Nerdy.ico"));
        AppWindow.Title = "Nerdy";
        ApplyMinimumSize();
        CenterOnScreen(1280, 800);
        // Closing (hide to the notification area, Ultra-Focus lock, quitting) is handled by AppLifetime.

        var navigation = App.GetService<NavigationService>();
        navigation.AttachRootFrame(RootFrame);
        RootFrame.Navigated += (_, e) => SetTitleBar((e.Content as ITitleBarHost)?.TitleBarElement);

        App.GetService<ThemeService>().Attach(RootGrid, AppWindow);
        App.GetService<ToastService>().Requested += (_, t) => ShowToast(t);
        RootGrid.Loaded += (_, _) => App.GetService<DialogService>().Attach(RootGrid.XamlRoot);

        _toastTimer = new DispatcherQueueTimerHolder(DispatcherQueue, TimeSpan.FromSeconds(2.8), HideToast);
    }

    public nint Handle => Win32Interop.GetWindowFromWindowId(AppWindow.Id);

    private bool _restoreMaximized;

    /// <summary>
    /// Focus sessions run full screen until <see cref="ExitSessionMode"/>. Ultra-Focus also keeps the window above
    /// every other window (<paramref name="alwaysOnTop"/>); plain Focus doesn't, so allowed apps can open over it.
    /// </summary>
    public void EnterSessionMode(bool alwaysOnTop)
    {
        if (AppWindow.Presenter is OverlappedPresenter overlapped) _restoreMaximized = overlapped.State == OverlappedPresenterState.Maximized;
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        if (alwaysOnTop) SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
    }

    public void ExitSessionMode()
    {
        SetWindowPos(Handle, HwndNotTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        ApplyMinimumSize();
        if (_restoreMaximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    private void ApplyMinimumSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        presenter.PreferredMinimumWidth = Scale(980);
        presenter.PreferredMinimumHeight = Scale(640);
    }

    /// <summary>Converts DIPs to physical pixels for this window's monitor.</summary>
    public int Scale(double dips) => (int)Math.Round(dips * GetDpiForWindow(Handle) / 96.0);

    private void CenterOnScreen(double width, double height)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var w = Math.Min(Scale(width), area.Width - Scale(32));
        var h = Math.Min(Scale(height), area.Height - Scale(32));
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    private void ShowToast(Toast toast)
    {
        ToastText.Text = toast.Message;
        (ToastIcon.Symbol, ToastIcon.Foreground) = toast.Kind switch
        {
            ToastKind.Error => (Symbol.ErrorCircle, new SolidColorBrush(ColorMix.Parse("#FF9A8F"))),
            ToastKind.Warning => (Symbol.Warning, new SolidColorBrush(ColorMix.Parse("#F5BD6E"))),
            ToastKind.Info => (Symbol.Info, new SolidColorBrush(ColorMix.Parse("#A7A7F0"))),
            _ => (Symbol.CheckmarkCircle, new SolidColorBrush(ColorMix.Parse("#7BE0B5")))
        };
        _toastAction = toast.Action;
        ToastAction.Content = toast.ActionLabel;
        ToastAction.Visibility = toast.ActionLabel is null ? Visibility.Collapsed : Visibility.Visible;

        ToastHost.Visibility = Visibility.Visible;
        Animate(1, 0, 350);
        _toastTimer.Restart(toast.Kind == ToastKind.Error || toast.ActionLabel is not null ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(2.8));
    }

    private void HideToast() => Animate(0, 14, 250, () => ToastHost.Visibility = Visibility.Collapsed);

    private void OnToastAction(object sender, RoutedEventArgs e)
    {
        _toastAction?.Invoke();
        HideToast();
    }

    private void Animate(double opacity, double y, int ms, Action? completed = null)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        var fade = new DoubleAnimation { To = opacity, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease };
        Storyboard.SetTarget(fade, ToastHost);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var move = new DoubleAnimation { To = y, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease };
        Storyboard.SetTarget(move, ToastTranslate);
        Storyboard.SetTargetProperty(move, "Y");
        sb.Children.Add(fade);
        sb.Children.Add(move);
        if (completed is not null) sb.Completed += (_, _) => completed();
        sb.Begin();
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    private const nint HwndTopmost = -1;
    private const nint HwndNotTopmost = -2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}

/// <summary>A one-shot timer that can be restarted (used for auto-hiding toasts).</summary>
internal sealed class DispatcherQueueTimerHolder
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

    public DispatcherQueueTimerHolder(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan interval, Action tick)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = interval;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => tick();
    }

    public void Restart(TimeSpan interval)
    {
        _timer.Stop();
        _timer.Interval = interval;
        _timer.Start();
    }
}
