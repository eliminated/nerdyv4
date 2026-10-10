using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Nerdy.App.Services.Theming;

/// <summary>
/// Colour helpers mirroring the prototype's CSS <c>color-mix(in srgb, c p%, base)</c>, plus theme-aware
/// tints so topic colours work on both light and dark surfaces.
/// </summary>
public static class ColorMix
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();

    public static Color Parse(string? hex, Color fallback = default)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length == 6) h = "FF" + h;
        return h.Length == 8 && uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : fallback;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary><paramref name="percent"/>% of <paramref name="color"/> mixed into <paramref name="baseColor"/>.</summary>
    public static Color Mix(Color color, double percent, Color baseColor)
    {
        var p = Math.Clamp(percent, 0, 100) / 100.0;
        byte L(byte a, byte b) => (byte)Math.Round(b + (a - b) * p);
        return Color.FromArgb(255, L(color.R, baseColor.R), L(color.G, baseColor.G), L(color.B, baseColor.B));
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    // Surfaces the tints are mixed into: white cards in light mode, the dark card colour in dark mode.
    public static readonly Color LightSurface = Colors.White;
    public static readonly Color DarkSurface = Color.FromArgb(255, 0x2A, 0x2A, 0x2E);

    /// <summary>A soft background tint of a colour (e.g. calendar event blocks).</summary>
    public static Color Tint(Color c, double lightPercent, bool dark) =>
        dark ? Mix(c, Math.Min(100, lightPercent * 2.1), DarkSurface) : Mix(c, lightPercent, LightSurface);

    /// <summary>Readable text in a colour family: darkened on light, lightened on dark.</summary>
    public static Color Ink(Color c, bool dark) =>
        dark ? Mix(c, 55, Colors.White) : Mix(c, 70, Colors.Black);

    public static SolidColorBrush Brush(Color c)
    {
        var key = ToHex(c) + c.A.ToString("X2");
        lock (BrushCache)
        {
            if (!BrushCache.TryGetValue(key, out var b)) BrushCache[key] = b = new SolidColorBrush(c);
            return b;
        }
    }

    public static SolidColorBrush Brush(string hex) => Brush(Parse(hex, Colors.Gray));
}
