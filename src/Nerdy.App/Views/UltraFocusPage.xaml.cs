using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Nerdy.App.Views;

public sealed partial class UltraFocusPage : Page, ITitleBarHost
{
    public UltraFocusPage()
    {
        ViewModel = App.GetService<FocusSessionViewModel>();
        Theme = App.GetService<ThemeService>();
        InitializeComponent();
        PaintBackground();

        // Holding needs press and release even though the Button handles pointer input itself.
        HoldButton.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => ViewModel.StartHold()), true);
        HoldButton.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => ViewModel.StopHold()), true);
        HoldButton.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => ViewModel.StopHold()), true);
        HoldButton.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => ViewModel.StopHold()), true);
        HoldButton.PointerExited += (_, _) => ViewModel.StopHold();
        // Keyboard: hold Enter or Space on the focused button.
        HoldButton.AddHandler(KeyDownEvent, new KeyEventHandler((_, e) => { if (e.Key is VirtualKey.Enter or VirtualKey.Space && !e.KeyStatus.WasKeyDown) ViewModel.StartHold(); }), true);
        HoldButton.AddHandler(KeyUpEvent, new KeyEventHandler((_, e) => { if (e.Key is VirtualKey.Enter or VirtualKey.Space) ViewModel.StopHold(); }), true);
    }

    public FocusSessionViewModel ViewModel { get; }
    public ThemeService Theme { get; }
    public UIElement TitleBarElement => DragRegion;

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    /// <summary>Deep navy with a soft glow of the accent colour in the middle, and an accent-tinted ring.</summary>
    private void PaintBackground()
    {
        var navy = ColorMix.Parse("#14132B");
        var glow = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.42),
            GradientOrigin = new Point(0.5, 0.42),
            RadiusX = 0.55,
            RadiusY = 0.6,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        glow.GradientStops.Add(new GradientStop { Color = ColorMix.Mix(Theme.Accent, 38, navy), Offset = 0 });
        glow.GradientStops.Add(new GradientStop { Color = navy, Offset = 0.72 });
        Root.Background = glow;
        Ring.RingBrush = new SolidColorBrush(ColorMix.Mix(Theme.Accent, 60, Color.FromArgb(255, 255, 255, 255)));
    }
}
