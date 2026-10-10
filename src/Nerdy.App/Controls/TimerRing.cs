using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Nerdy.App.Controls;

/// <summary>A countdown ring: a full track plus an arc for the remaining fraction, starting at 12 o'clock.</summary>
public sealed class TimerRing : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TimerRing), new PropertyMetadata(1d, (d, _) => ((TimerRing)d).Draw()));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(TimerRing), new PropertyMetadata(12d, (d, _) => ((TimerRing)d).Draw()));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(TimerRing), new PropertyMetadata(null, (d, e) => ((TimerRing)d)._track.Stroke = (Brush)e.NewValue));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush), typeof(Brush), typeof(TimerRing), new PropertyMetadata(null, (d, e) => ((TimerRing)d)._arc.Stroke = (Brush)e.NewValue));

    public static readonly DependencyProperty CenterBrushProperty = DependencyProperty.Register(
        nameof(CenterBrush), typeof(Brush), typeof(TimerRing), new PropertyMetadata(null, (d, e) => ((TimerRing)d)._track.Fill = (Brush)e.NewValue));

    private readonly Ellipse _track = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _arc = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    public TimerRing()
    {
        Children.Add(_track);
        Children.Add(_arc);
        SizeChanged += (_, _) => Draw();
    }

    /// <summary>Remaining fraction, 0–1.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush? RingBrush { get => (Brush?)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
    public Brush? CenterBrush { get => (Brush?)GetValue(CenterBrushProperty); set => SetValue(CenterBrushProperty, value); }

    private void Draw()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var t = Thickness;
        _track.StrokeThickness = t;
        _arc.StrokeThickness = t;

        var r = (size - t) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var value = Math.Clamp(Value, 0, 1);
        if (value <= 0.0005) { _arc.Data = null; return; }

        // A full circle can't be one arc segment; stop just short of it.
        var angle = Math.Min(value, 0.9999) * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = angle > Math.PI
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _arc.Data = geometry;
    }
}
