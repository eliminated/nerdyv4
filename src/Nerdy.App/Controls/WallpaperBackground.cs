using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Services.Theming;
using Windows.Foundation;
using Windows.UI;

namespace Nerdy.App.Controls;

/// <summary>
/// The prototype's soft wallpapers (Bloom, Mist, Meadow, Paper) built from layered radial gradients,
/// with dark-theme counterparts. Bloom picks up the accent colour.
/// </summary>
public sealed class WallpaperBackground : Grid
{
    public static readonly DependencyProperty WallpaperProperty = DependencyProperty.Register(
        nameof(Wallpaper), typeof(string), typeof(WallpaperBackground), new PropertyMetadata("bloom", (d, _) => ((WallpaperBackground)d).Render()));

    private readonly ThemeService _theme = App.GetService<ThemeService>();

    public WallpaperBackground()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { _theme.Changed += OnThemeChanged; Render(); };
        Unloaded += (_, _) => _theme.Changed -= OnThemeChanged;
    }

    public string Wallpaper
    {
        get => (string)GetValue(WallpaperProperty);
        set => SetValue(WallpaperProperty, value);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        Children.Clear();
        var dark = _theme.IsDark;
        var a = _theme.Accent;
        Color C(string hex) => ColorMix.Parse(hex);

        // (base colour, [(colour, centreX, centreY, radiusX, radiusY)])
        var (baseColor, blobs) = (Wallpaper, dark) switch
        {
            ("mist", false) => (C("#F2F5F9"), new[] { (C("#D6E5F5"), .82, .10, .70, .80), (C("#E2E7F1"), .10, .90, .60, .60) }),
            ("mist", true) => (C("#1A1E25"), new[] { (C("#22344A"), .82, .10, .70, .80), (C("#262B36"), .10, .90, .60, .60) }),
            ("meadow", false) => (C("#F3F6F1"), new[] { (C("#D3EDDD"), .15, .20, .70, .70), (C("#EEF0D0"), .90, .80, .60, .60) }),
            ("meadow", true) => (C("#191F1B"), new[] { (C("#1F3A2A"), .15, .20, .70, .70), (C("#33361E"), .90, .80, .60, .60) }),
            ("paper", false) => (C("#F5F2EC"), Array.Empty<(Color, double, double, double, double)>()),
            ("paper", true) => (C("#22201C"), Array.Empty<(Color, double, double, double, double)>()),
            (_, false) => (C("#F5F4FA"), new[] { (ColorMix.Mix(a, 28, C("#FFFFFF")), .18, .15, .60, .70), (C("#F6DCE8"), .88, .88, .55, .60) }),
            (_, true) => (C("#1B1A24"), new[] { (ColorMix.Mix(a, 45, C("#1B1A24")), .18, .15, .60, .70), (C("#3D2533"), .88, .88, .55, .60) })
        };

        Background = new SolidColorBrush(baseColor);
        foreach (var (color, cx, cy, rx, ry) in blobs)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(cx, cy),
                GradientOrigin = new Point(cx, cy),
                RadiusX = rx,
                RadiusY = ry,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            brush.GradientStops.Add(new GradientStop { Color = color, Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = ColorMix.WithAlpha(color, 0), Offset = 0.7 });
            Children.Add(new Border { Background = brush });
        }
    }
}
