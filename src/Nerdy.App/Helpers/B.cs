using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Nerdy.App.Helpers;

/// <summary>Small pure functions for x:Bind expressions (e.g. <c>{x:Bind h:B.Not(ViewModel.IsBusy)}</c>).</summary>
public static class B
{
    public static bool Not(bool value) => !value;
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility HasText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility HasItems(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility NoItems(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public static double Opacity(bool visible) => visible ? 1 : 0;
    public static double Dim(bool dimmed) => dimmed ? 0.45 : 1;
    public static double FadeWhen(bool hidden) => hidden ? 0 : 1;
    public static GridLength Pixels(double value) => new(value);
    public static Thickness Pad(double horizontal, double vertical) => new(horizontal, vertical, horizontal, vertical);
    public static Thickness TopMargin(double top) => new(0, top, 0, 0);
    /// <summary>Picks one of two Nerdy theme brushes (for the theme the window shows).</summary>
    public static Brush Brush(bool condition, string whenTrue, string whenFalse) =>
        App.GetService<Services.Theming.ThemeService>().Brush(condition ? whenTrue : whenFalse);
    /// <summary>Places a 16 px tall chart grid line (in a host that overhangs the plot by 8 px) <paramref name="bottom"/> px above the baseline.</summary>
    public static Thickness GridLineMargin(double bottom) => new(0, 0, 0, bottom);
    /// <summary>Position of the agenda's "now" line (starts just left of the event column).</summary>
    public static Thickness NowMargin(double top) => new(60, top, 0, 0);
}
