using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Nerdy.App.Controls;

/// <summary>An item that takes a share of a <see cref="ShareBarPanel"/>'s width.</summary>
public interface IShare
{
    double Share { get; }
}

/// <summary>
/// Lays children out side by side, each as wide as its DataContext's <see cref="IShare.Share"/> of the total
/// (the stacked "time by subject" bar).
/// </summary>
public sealed class ShareBarPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ShareBarPanel), new PropertyMetadata(2d, (d, _) => ((ShareBarPanel)d).InvalidateArrange()));

    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private static double ShareOf(UIElement child) => Math.Max(0, ((child as FrameworkElement)?.DataContext as IShare)?.Share ?? 0);

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => ShareOf(c) > 0).ToList();
        var total = visible.Sum(ShareOf);
        var room = Math.Max(0, finalSize.Width - Spacing * Math.Max(0, visible.Count - 1));
        var x = 0d;
        foreach (var child in Children)
        {
            var share = ShareOf(child);
            if (share <= 0 || total <= 0)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var width = share / total * room;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}
