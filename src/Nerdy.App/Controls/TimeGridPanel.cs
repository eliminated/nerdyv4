using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Nerdy.App.Controls;

/// <summary>Anything placed on a time grid: a span of hours, optionally sharing the width with overlapping items.</summary>
public interface ITimeSlot
{
    double StartHour { get; }
    double EndHour { get; }
    /// <summary>Zero-based lane among <see cref="Lanes"/> side-by-side lanes (for overlapping events).</summary>
    int Lane { get; }
    int Lanes { get; }
}

/// <summary>
/// Lays children out vertically by time: each child's DataContext is an <see cref="ITimeSlot"/>. Used for calendar
/// day columns, hour lines and labels, the "now" line, and the study-block preview.
/// </summary>
public sealed class TimeGridPanel : Panel
{
    public static readonly DependencyProperty HourHeightProperty = DependencyProperty.Register(
        nameof(HourHeight), typeof(double), typeof(TimeGridPanel), new PropertyMetadata(52d, (d, _) => ((TimeGridPanel)d).InvalidateMeasure()));

    public static readonly DependencyProperty FirstHourProperty = DependencyProperty.Register(
        nameof(FirstHour), typeof(double), typeof(TimeGridPanel), new PropertyMetadata(0d, (d, _) => ((TimeGridPanel)d).InvalidateMeasure()));

    public static readonly DependencyProperty HoursProperty = DependencyProperty.Register(
        nameof(Hours), typeof(double), typeof(TimeGridPanel), new PropertyMetadata(24d, (d, _) => ((TimeGridPanel)d).InvalidateMeasure()));

    /// <summary>Horizontal gap kept around each item (the prototype insets blocks by 4 px).</summary>
    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset), typeof(double), typeof(TimeGridPanel), new PropertyMetadata(4d, (d, _) => ((TimeGridPanel)d).InvalidateArrange()));

    /// <summary>Items shorter than this are stretched so their title stays readable.</summary>
    public static readonly DependencyProperty MinItemHeightProperty = DependencyProperty.Register(
        nameof(MinItemHeight), typeof(double), typeof(TimeGridPanel), new PropertyMetadata(18d, (d, _) => ((TimeGridPanel)d).InvalidateArrange()));

    public double HourHeight { get => (double)GetValue(HourHeightProperty); set => SetValue(HourHeightProperty, value); }
    public double FirstHour { get => (double)GetValue(FirstHourProperty); set => SetValue(FirstHourProperty, value); }
    public double Hours { get => (double)GetValue(HoursProperty); set => SetValue(HoursProperty, value); }
    public double Inset { get => (double)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }
    public double MinItemHeight { get => (double)GetValue(MinItemHeightProperty); set => SetValue(MinItemHeightProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        foreach (var child in Children)
        {
            var (_, h, w) = Place(child, width);
            // Zero-length slots (lines, labels, the now marker) take their natural height.
            var isPoint = (child as FrameworkElement)?.DataContext is ITimeSlot s && s.EndHour <= s.StartHour;
            child.Measure(new Size(Math.Max(0, w), isPoint ? double.PositiveInfinity : Math.Max(0, h)));
        }
        return new Size(width, Hours * HourHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var (y, h, w) = Place(child, finalSize.Width);
            var slot = (child as FrameworkElement)?.DataContext as ITimeSlot;
            var lanes = Math.Max(1, slot?.Lanes ?? 1);
            var x = finalSize.Width / lanes * (slot?.Lane ?? 0) + (slot is null ? 0 : Inset);
            child.Arrange(new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));
        }
        return finalSize;
    }

    private (double Y, double Height, double Width) Place(UIElement child, double width)
    {
        if ((child as FrameworkElement)?.DataContext is not ITimeSlot slot) return (0, 0, width);
        var start = Math.Clamp(slot.StartHour, FirstHour, FirstHour + Hours);
        var end = Math.Clamp(slot.EndHour, FirstHour, FirstHour + Hours);
        var y = (start - FirstHour) * HourHeight;
        var h = slot.EndHour <= slot.StartHour ? child.DesiredSize.Height : Math.Max(MinItemHeight, (end - start) * HourHeight - 3);
        var lanes = Math.Max(1, slot.Lanes);
        return (y, h, width / lanes - 2 * Inset);
    }

    /// <summary>Assigns lanes so overlapping items sit side by side (greedy, per overlap cluster).</summary>
    public static void AssignLanes<T>(IReadOnlyList<T> items, Func<T, double> start, Func<T, double> end, Action<T, int, int> set)
    {
        var sorted = items.OrderBy(start).ThenByDescending(end).ToList();
        var cluster = new List<(T Item, int Lane)>();
        var laneEnds = new List<double>();
        var clusterEnd = double.MinValue;

        void Flush()
        {
            foreach (var (item, lane) in cluster) set(item, lane, laneEnds.Count);
            cluster.Clear();
            laneEnds.Clear();
        }

        foreach (var item in sorted)
        {
            if (start(item) >= clusterEnd) Flush();
            var lane = laneEnds.FindIndex(e => e <= start(item));
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(end(item)); }
            else laneEnds[lane] = end(item);
            cluster.Add((item, lane));
            clusterEnd = Math.Max(clusterEnd == double.MinValue ? end(item) : clusterEnd, end(item));
        }
        Flush();
    }
}
