using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Nerdy.App.Services.Theming;
using Windows.UI;

namespace Nerdy.App.Controls;

public enum OrbitMode { Aura, Horizon }

/// <summary>
/// The prototype's "orbit" effect behind the selected sidebar item. Aura: three soft radial blobs that breathe
/// and drift. Horizon: a gradient line along the bottom edge with a slow travelling light. Everything is a
/// Composition animation, so it costs nothing on the UI thread.
/// </summary>
public sealed class OrbitGlow : Grid
{
    public static readonly DependencyProperty ColorsProperty = DependencyProperty.Register(
        nameof(Colors), typeof(string), typeof(OrbitGlow), new PropertyMetadata("#38BDF8,#3B82F6,#6366F1", (d, _) => ((OrbitGlow)d).Rebuild()));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(OrbitMode), typeof(OrbitGlow), new PropertyMetadata(OrbitMode.Aura, (d, _) => ((OrbitGlow)d).Rebuild()));

    private ContainerVisual? _container;
    private readonly Random _random = new();

    public OrbitGlow()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Rebuild();
        Unloaded += (_, _) => Teardown();
        SizeChanged += (_, _) => Rebuild();
    }

    /// <summary>Comma-separated hex colours, sampled along the gradient.</summary>
    public string Colors
    {
        get => (string)GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    public OrbitMode Mode
    {
        get => (OrbitMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private void Teardown()
    {
        _container?.Children.RemoveAll();
        _container?.Dispose();
        _container = null;
        ElementCompositionPreview.SetElementChildVisual(this, null);
    }

    private void Rebuild()
    {
        Teardown();
        if (!IsLoaded || ActualWidth < 1 || ActualHeight < 1) return;

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _container = compositor.CreateContainerVisual();
        _container.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
        _container.Clip = compositor.CreateInsetClip();
        var palette = (Colors ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => ColorMix.Parse(c)).ToArray();
        if (palette.Length == 0) palette = [ColorMix.Parse("#5B5BD6")];

        if (Mode == OrbitMode.Horizon) BuildHorizon(compositor, palette);
        else BuildAura(compositor, palette);
        ElementCompositionPreview.SetElementChildVisual(this, _container);
    }

    private void BuildAura(Compositor c, Color[] palette)
    {
        float w = (float)ActualWidth, h = (float)ActualHeight;
        for (var i = 0; i < 3; i++)
        {
            var color = Sample(palette, i / 2f);
            var r = w * 0.36f;
            var blob = c.CreateSpriteVisual();
            blob.Size = new Vector2(r * 2, r * 2);
            blob.Brush = RadialBrush(c, color, 0.34f, 0.12f);
            blob.AnchorPoint = new Vector2(0.5f, 0.5f);

            // Drift through a loop of points that follows the prototype's sine paths.
            var phase = (float)(_random.NextDouble() * Math.Tau);
            var fx = 0.05f + (float)_random.NextDouble() * 0.05f;
            var fy = 0.07f + (float)_random.NextDouble() * 0.06f;
            var period = 1f / Math.Min(fx, fy);
            var drift = c.CreateVector3KeyFrameAnimation();
            const int steps = 8;
            for (var k = 0; k <= steps; k++)
            {
                var t = period * k / steps;
                drift.InsertKeyFrame(k / (float)steps, new Vector3(
                    w * (0.5f + 0.42f * MathF.Sin(t * fx * MathF.Tau + phase)),
                    h * (0.5f + 0.7f * MathF.Sin(t * fy * MathF.Tau + phase * 1.7f)), 0));
            }
            drift.Duration = TimeSpan.FromSeconds(period);
            drift.IterationBehavior = AnimationIterationBehavior.Forever;
            blob.StartAnimation("Offset", drift);

            var breathe = c.CreateVector3KeyFrameAnimation();
            breathe.InsertKeyFrame(0, Vector3.One);
            breathe.InsertKeyFrame(0.5f, new Vector3(1.18f, 1.18f, 1));
            breathe.InsertKeyFrame(1, Vector3.One);
            breathe.Duration = TimeSpan.FromSeconds(9 + i * 2);
            breathe.IterationBehavior = AnimationIterationBehavior.Forever;
            blob.StartAnimation("Scale", breathe);

            _container!.Children.InsertAtTop(blob);
        }
    }

    private void BuildHorizon(Compositor c, Color[] palette)
    {
        float w = (float)ActualWidth, h = (float)ActualHeight;

        var glow = c.CreateSpriteVisual();
        glow.Size = new Vector2(w * 1.1f, h * 1.2f);
        glow.Offset = new Vector3(-w * 0.05f, h * 0.35f, 0);
        glow.Brush = RadialBrush(c, Sample(palette, 0.5f), 0.26f, 0.08f);
        _container!.Children.InsertAtTop(glow);

        var line = c.CreateSpriteVisual();
        line.Size = new Vector2(w, 1.5f);
        line.Offset = new Vector3(0, h - 2f, 0);
        var lg = c.CreateLinearGradientBrush();
        lg.StartPoint = new Vector2(0, 0.5f);
        lg.EndPoint = new Vector2(1, 0.5f);
        lg.ColorStops.Add(c.CreateColorGradientStop(0, ColorMix.WithAlpha(Sample(palette, 0), 0)));
        lg.ColorStops.Add(c.CreateColorGradientStop(0.2f, ColorMix.WithAlpha(Sample(palette, 0.15f), 230)));
        lg.ColorStops.Add(c.CreateColorGradientStop(0.5f, Sample(palette, 0.5f)));
        lg.ColorStops.Add(c.CreateColorGradientStop(0.8f, ColorMix.WithAlpha(Sample(palette, 0.85f), 230)));
        lg.ColorStops.Add(c.CreateColorGradientStop(1, ColorMix.WithAlpha(Sample(palette, 1), 0)));
        line.Brush = lg;
        _container.Children.InsertAtTop(line);

        var light = c.CreateSpriteVisual();
        light.Size = new Vector2(52, 10);
        light.AnchorPoint = new Vector2(0.5f, 0.5f);
        light.Brush = RadialBrush(c, Color.FromArgb(255, 255, 255, 255), 0.95f, 0.3f);
        var travel = c.CreateScalarKeyFrameAnimation();
        travel.InsertKeyFrame(0, -0.2f * w);
        travel.InsertKeyFrame(1, 1.2f * w, c.CreateLinearEasingFunction());
        travel.Duration = TimeSpan.FromSeconds(11);
        travel.IterationBehavior = AnimationIterationBehavior.Forever;
        light.Offset = new Vector3(0, h - 1.25f, 0);
        light.StartAnimation("Offset.X", travel);
        _container.Children.InsertAtTop(light);
    }

    private static CompositionRadialGradientBrush RadialBrush(Compositor c, Color color, float centerAlpha, float midAlpha)
    {
        var b = c.CreateRadialGradientBrush();
        b.ColorStops.Add(c.CreateColorGradientStop(0, ColorMix.WithAlpha(color, (byte)(255 * centerAlpha))));
        b.ColorStops.Add(c.CreateColorGradientStop(0.55f, ColorMix.WithAlpha(color, (byte)(255 * midAlpha))));
        b.ColorStops.Add(c.CreateColorGradientStop(1, ColorMix.WithAlpha(color, 0)));
        return b;
    }

    private static Color Sample(Color[] palette, float t)
    {
        if (palette.Length == 1) return palette[0];
        var x = Math.Clamp(t, 0, 0.9999f) * (palette.Length - 1);
        var i = (int)x;
        var f = x - i;
        var a = palette[i];
        var b = palette[i + 1];
        return Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
    }
}
