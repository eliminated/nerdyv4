using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Windows.UI;

namespace Nerdy.App.Services.Theming;

/// <summary>
/// Applies appearance preferences: light/dark/system theme, accent colour (recolouring the shared accent brush
/// instances so every {ThemeResource} updates live), font family and density. View models listen to
/// <see cref="Changed"/> to recompute colours they derive themselves (topic tints etc.).
/// </summary>
public sealed partial class ThemeService : ObservableObject
{
    private FrameworkElement? _root;
    private AppWindow? _appWindow;
    private string _appliedAccent = "";

    public event EventHandler? Changed;

    public Color Accent { get; private set; } = ColorMix.Parse(Accents.Default);

    [ObservableProperty]
    public partial bool IsDark { get; private set; }

    [ObservableProperty]
    public partial double DensityFactor { get; private set; } = 1;

    [ObservableProperty]
    public partial FontFamily FontFamily { get; private set; } = new("Segoe UI Variable Text, Segoe UI");

    public void Attach(FrameworkElement root, AppWindow appWindow)
    {
        if (_root is not null) _root.ActualThemeChanged -= OnActualThemeChanged;
        _root = root;
        _appWindow = appWindow;
        root.ActualThemeChanged += OnActualThemeChanged;
        IsDark = root.ActualTheme == ElementTheme.Dark;
        UpdateCaptionButtons();
    }

    public void Apply(Preferences preferences)
    {
        if (_root is not null)
            _root.RequestedTheme = preferences.Theme switch
            {
                AppThemeChoice.Light => ElementTheme.Light,
                AppThemeChoice.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

        var refresh = false;
        if (!string.Equals(_appliedAccent, preferences.Accent, StringComparison.OrdinalIgnoreCase))
        {
            refresh = _appliedAccent.Length > 0;
            _appliedAccent = preferences.Accent;
            Accent = ColorMix.Parse(preferences.Accent, ColorMix.Parse(Accents.Default));
            RecolorAccentBrushes(Accent);
            SetSystemAccentColors(Accent);
        }

        var font = Fonts.Get(preferences.Font);
        if (FontFamily.Source != font.Family)
        {
            FontFamily = new FontFamily(font.Family);
            // Pages inherit FontFamily, but controls (buttons, segments, text boxes) take it from this theme resource.
            Application.Current.Resources["ContentControlThemeFontFamily"] = FontFamily;
            refresh = true;
        }
        if (refresh) RefreshThemeResources();
        DensityFactor = preferences.Density == Density.Compact ? 0.8 : 1;

        if (_root is not null) IsDark = _root.ActualTheme == ElementTheme.Dark;
        UpdateCaptionButtons();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A Nerdy theme brush for the theme the window is actually showing. (Looking resources up through
    /// Application.Resources would return the system theme's brush, which is wrong when the user overrides it.)
    /// Accent brushes are shared instances, so the result keeps tracking accent changes.
    /// </summary>
    public Brush Brush(string key)
    {
        var wanted = IsDark ? "Dark" : "Light";
        foreach (var (theme, dictionary) in AllThemeDictionaries())
            if (theme == wanted && dictionary.TryGetValue(key, out var value) && value is Brush b)
                return b;
        return Application.Current.Resources.TryGetValue(key, out var fallback) && fallback is Brush fb ? fb : new SolidColorBrush(Colors.Magenta);
    }

    /// <summary>Theme-aware tint of a colour for soft backgrounds (calendar blocks, chips…).</summary>
    public Color Tint(Color c, double lightPercent) => ColorMix.Tint(c, lightPercent, IsDark);

    /// <summary>Readable text colour in the family of <paramref name="c"/>.</summary>
    public Color Ink(Color c) => ColorMix.Ink(c, IsDark);

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        IsDark = sender.ActualTheme == ElementTheme.Dark;
        UpdateCaptionButtons();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void RecolorAccentBrushes(Color a)
    {
        var white = Colors.White;
        var black = Colors.Black;
        var dark = Color.FromArgb(255, 0x26, 0x26, 0x2A);

        var light = new Dictionary<string, Color>
        {
            ["NerdyAccentBrush"] = a,
            ["NerdyAccentSoftBrush"] = ColorMix.Mix(a, 9, white),
            ["NerdyAccentSoftBorderBrush"] = ColorMix.Mix(a, 18, white),
            ["NerdyAccentTint8Brush"] = ColorMix.Mix(a, 8, white),
            ["NerdyAccentTint14Brush"] = ColorMix.Mix(a, 14, white),
            ["NerdyAccentTint30Brush"] = ColorMix.Mix(a, 30, white),
            ["NerdyAccentTextBrush"] = ColorMix.Mix(a, 80, black),
            ["AccentFillColorDefaultBrush"] = a,
            ["AccentFillColorSecondaryBrush"] = ColorMix.WithAlpha(a, 0xE6),
            ["AccentFillColorTertiaryBrush"] = ColorMix.WithAlpha(a, 0xCC),
            ["AccentTextFillColorPrimaryBrush"] = ColorMix.Mix(a, 80, black),
            ["AccentTextFillColorSecondaryBrush"] = ColorMix.Mix(a, 80, black),
            ["AccentTextFillColorTertiaryBrush"] = a,
            ["TextControlFocusedBorderBrush"] = a
        };
        var darkColors = new Dictionary<string, Color>
        {
            ["NerdyAccentBrush"] = a,
            ["NerdyAccentSoftBrush"] = ColorMix.Mix(a, 20, dark),
            ["NerdyAccentSoftBorderBrush"] = ColorMix.Mix(a, 34, dark),
            ["NerdyAccentTint8Brush"] = ColorMix.Mix(a, 14, dark),
            ["NerdyAccentTint14Brush"] = ColorMix.Mix(a, 22, dark),
            ["NerdyAccentTint30Brush"] = ColorMix.Mix(a, 40, dark),
            ["NerdyAccentTextBrush"] = ColorMix.Mix(a, 55, white),
            ["AccentFillColorDefaultBrush"] = a,
            ["AccentFillColorSecondaryBrush"] = ColorMix.WithAlpha(a, 0xE6),
            ["AccentFillColorTertiaryBrush"] = ColorMix.WithAlpha(a, 0xCC),
            ["AccentTextFillColorPrimaryBrush"] = ColorMix.Mix(a, 55, white),
            ["AccentTextFillColorSecondaryBrush"] = ColorMix.Mix(a, 55, white),
            ["AccentTextFillColorTertiaryBrush"] = ColorMix.Mix(a, 70, white),
            ["TextControlFocusedBorderBrush"] = ColorMix.Mix(a, 70, white)
        };

        foreach (var dictionary in AllThemeDictionaries())
        {
            var colors = dictionary.Key switch { "Light" => light, "Dark" => darkColors, _ => null };
            if (colors is null) continue;
            foreach (var (key, color) in colors)
                if (dictionary.Value.TryGetValue(key, out var value) && value is SolidColorBrush brush)
                    brush.Color = color;
            if (dictionary.Value.TryGetValue("TextControlElevationBorderFocusedBrush", out var g) && g is LinearGradientBrush underline)
                underline.GradientStops[0].Color = colors["TextControlFocusedBorderBrush"];
        }
    }

    /// <summary>
    /// Some WinUI templates reference the SystemAccentColor family directly (as colours, not brushes).
    /// Overriding them at app level covers those; <see cref="RefreshThemeResources"/> makes live elements pick them up.
    /// </summary>
    private static void SetSystemAccentColors(Color a)
    {
        var resources = Application.Current.Resources;
        resources["SystemAccentColor"] = a;
        resources["SystemAccentColorLight1"] = ColorMix.Mix(a, 75, Colors.White);
        resources["SystemAccentColorLight2"] = ColorMix.Mix(a, 55, Colors.White);
        resources["SystemAccentColorLight3"] = ColorMix.Mix(a, 35, Colors.White);
        resources["SystemAccentColorDark1"] = ColorMix.Mix(a, 85, Colors.Black);
        resources["SystemAccentColorDark2"] = ColorMix.Mix(a, 70, Colors.Black);
        resources["SystemAccentColorDark3"] = ColorMix.Mix(a, 55, Colors.Black);
    }

    /// <summary>Forces {ThemeResource} references to re-resolve by briefly flipping the root's theme.</summary>
    private void RefreshThemeResources()
    {
        if (_root is null) return;
        var requested = _root.RequestedTheme;
        _root.RequestedTheme = _root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        _root.RequestedTheme = requested;
    }

    /// <summary>The theme dictionaries of Nerdy's own colour resources (Themes/Colors.xaml).</summary>
    private static IEnumerable<KeyValuePair<string, ResourceDictionary>> AllThemeDictionaries()
    {
        static IEnumerable<ResourceDictionary> Walk(ResourceDictionary d)
        {
            yield return d;
            foreach (var m in d.MergedDictionaries)
                foreach (var x in Walk(m)) yield return x;
        }

        foreach (var d in Walk(Application.Current.Resources))
            foreach (var (key, value) in d.ThemeDictionaries)
                if (key is string k && value is ResourceDictionary rd && rd.ContainsKey("NerdyAccentBrush"))
                    yield return new(k, rd);
    }

    private void UpdateCaptionButtons()
    {
        if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported()) return;
        var bar = _appWindow.TitleBar;
        var fg = IsDark ? Colors.White : Color.FromArgb(255, 0x1B, 0x1B, 0x1F);
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = fg;
        bar.ButtonInactiveForegroundColor = ColorMix.WithAlpha(fg, 0x80);
        bar.ButtonHoverBackgroundColor = IsDark ? Color.FromArgb(0x18, 255, 255, 255) : Color.FromArgb(0x0D, 0, 0, 0);
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonPressedBackgroundColor = IsDark ? Color.FromArgb(0x10, 255, 255, 255) : Color.FromArgb(0x14, 0, 0, 0);
        bar.ButtonPressedForegroundColor = fg;
    }
}
