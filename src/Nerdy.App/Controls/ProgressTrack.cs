using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Services.Theming;

namespace Nerdy.App.Controls;

/// <summary>The prototype's rounded progress track: a soft grey rail with a coloured fill (0–100).</summary>
public sealed class ProgressTrack : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ProgressTrack), new PropertyMetadata(0d, (d, _) => ((ProgressTrack)d).Layout()));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ProgressTrack), new PropertyMetadata(null, (d, e) => ((ProgressTrack)d)._fill.Background = (Brush)e.NewValue));

    private readonly ThemeService _theme = App.GetService<ThemeService>();
    private readonly Border _rail = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left };

    public ProgressTrack()
    {
        Height = 6;
        Children.Add(_rail);
        Children.Add(_fill);
        SizeChanged += (_, _) => Layout();
        Loaded += (_, _) => { _theme.Changed += OnThemeChanged; OnThemeChanged(null, EventArgs.Empty); };
        Unloaded += (_, _) => _theme.Changed -= OnThemeChanged;
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    // The rail uses the theme the window shows (Application.Resources lookups would follow the system theme).
    private void OnThemeChanged(object? sender, EventArgs e) => _rail.Background = _theme.Brush("NerdyTrackBrush");

    private void Layout()
    {
        var radius = new CornerRadius(ActualHeight / 2);
        CornerRadius = radius;
        _fill.CornerRadius = radius;
        _fill.Width = Math.Max(0, ActualWidth * Math.Clamp(Value, 0, 100) / 100);
    }
}
