using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Nerdy.App.Controls;

/// <summary>
/// The prototype's sidebar toggle: three lines that morph from a hamburger (collapsed) into a back arrow
/// (expanded) with a springy ease. Only transforms are animated, so it runs on the compositor.
/// </summary>
public sealed class PaneToggleGlyph : UserControl
{
    private const double Size = 18;
    private readonly (Rectangle Line, CompositeTransform T)[] _lines = new (Rectangle, CompositeTransform)[3];

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(PaneToggleGlyph), new PropertyMetadata(true, (d, _) => ((PaneToggleGlyph)d).Animate(true)));

    public PaneToggleGlyph()
    {
        var canvas = new Canvas { Width = Size, Height = 14 };
        double[] tops = [0, 6.2, 12.4];
        for (var i = 0; i < 3; i++)
        {
            var t = new CompositeTransform { CenterX = 0, CenterY = 0.8 };
            var line = new Rectangle { Width = Size, Height = 1.6, RadiusX = 1, RadiusY = 1, RenderTransform = t };
            line.SetBinding(Shape.FillProperty, new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            Canvas.SetTop(line, tops[i]);
            canvas.Children.Add(line);
            _lines[i] = (line, t);
        }
        Content = canvas;
        Loaded += (_, _) => Animate(false);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private void Animate(bool animated)
    {
        // (width, rotation, translateY) per line for each state.
        (double W, double R, double Y)[] target = IsOpen
            ? [(9, -42, 6.2), (18, 0, 0), (9, 42, -6.2)]
            : [(18, 0, 0), (13, 0, 0), (18, 0, 0)];

        var sb = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(animated ? 400 : 0);
        var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        for (var i = 0; i < 3; i++)
        {
            var t = _lines[i].T;
            Add(sb, t, nameof(CompositeTransform.ScaleX), target[i].W / Size, duration, ease);
            Add(sb, t, nameof(CompositeTransform.Rotation), target[i].R, duration, ease);
            Add(sb, t, nameof(CompositeTransform.TranslateY), target[i].Y, duration, ease);
        }
        sb.Begin();
    }

    private static void Add(Storyboard sb, DependencyObject target, string property, double to, TimeSpan duration, EasingFunctionBase ease)
    {
        var a = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        sb.Children.Add(a);
    }
}
