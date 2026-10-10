using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Services.Theming;

namespace Nerdy.App.ViewModels.Common;

public enum ChoiceLook
{
    /// <summary>Large selectable card with an icon tile (role cards, Focus mode cards).</summary>
    Card,
    /// <summary>Compact chip that fills with the accent when selected (durations, days, subjects).</summary>
    Chip,
    /// <summary>Chip with a coloured dot that tints with its own colour when selected (topics).</summary>
    ColorChip,
    /// <summary>Segment of a segmented control (white raised pill when selected).</summary>
    Segment
}

/// <summary>
/// A selectable option whose colours are computed from its state, like the prototype's logic does
/// (bg/bd/fg per item). Call <see cref="Refresh"/> after a theme change.
/// </summary>
public sealed partial class ChoiceItem : ObservableObject
{
    private readonly ThemeService _theme;
    private readonly Action<ChoiceItem>? _onSelect;

    public ChoiceItem(ThemeService theme, ChoiceLook look, string label, object? value, Action<ChoiceItem>? onSelect,
        Symbol? icon = null, string? description = null, string? colorHex = null, bool selected = false)
    {
        _theme = theme;
        _onSelect = onSelect;
        Look = look;
        Label = label;
        Value = value;
        _baseIcon = icon ?? Symbol.Circle;
        HasIcon = icon is not null;
        Description = description;
        ColorHex = colorHex;
        IsSelected = selected;
        Refresh();
    }

    public ChoiceLook Look { get; }
    public string Label { get; }
    public object? Value { get; }
    private readonly Symbol _baseIcon;

    /// <summary>The icon, swapped for <see cref="SelectedIcon"/> while selected (e.g. a checkmark).</summary>
    public Symbol Icon => IsSelected && SelectedIcon is { } swap ? swap : _baseIcon;
    public bool HasIcon { get; }
    public string? Description { get; }
    public string? ColorHex { get; }
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasDot => ColorHex is not null;

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsEnabled { get; set; } = true;
    [ObservableProperty] public partial string? ToolTip { get; set; }

    [ObservableProperty] public partial Brush Background { get; private set; } = null!;
    [ObservableProperty] public partial Brush BorderBrush { get; private set; } = null!;
    [ObservableProperty] public partial Brush Foreground { get; private set; } = null!;
    [ObservableProperty] public partial Brush IconBackground { get; private set; } = null!;
    [ObservableProperty] public partial Brush IconForeground { get; private set; } = null!;
    [ObservableProperty] public partial Brush DotBrush { get; private set; } = null!;
    [ObservableProperty] public partial Thickness BorderThickness { get; private set; }
    [ObservableProperty] public partial double Lift { get; private set; }
    [ObservableProperty] public partial Windows.UI.Text.FontWeight Weight { get; private set; }

    /// <summary>Swaps the icon (e.g. to a checkmark when an onboarding subject is picked).</summary>
    public Symbol? SelectedIcon { get; init; }

    [RelayCommand]
    private void Select()
    {
        if (IsEnabled) _onSelect?.Invoke(this);
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(Icon));
        Refresh();
    }
    partial void OnIsEnabledChanged(bool value) => Refresh();

    public void Refresh()
    {
        var on = IsSelected;
        Brush B(string key) => _theme.Brush(key);
        var color = ColorMix.Parse(ColorHex, _theme.Accent);
        Weight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Medium;
        Lift = 0;
        DotBrush = ColorMix.Brush(color);

        switch (Look)
        {
            case ChoiceLook.Card:
                Background = B(on ? "NerdyAccentTint8Brush" : "NerdyCardBackgroundBrush");
                BorderBrush = B(on ? "NerdyAccentBrush" : "NerdyCardBorderBrush");
                BorderThickness = new Thickness(2);
                IconBackground = B(on ? "NerdyAccentBrush" : "NerdyIconTileBrush");
                IconForeground = B(on ? "NerdyOnAccentBrush" : "NerdyTextStrongBrush");
                Foreground = B("NerdyTextPrimaryBrush");
                Lift = on ? -2 : 0;
                break;
            case ChoiceLook.Chip:
                Background = B(on ? "NerdyAccentBrush" : "NerdyCardBackgroundBrush");
                BorderBrush = B(on ? "NerdyAccentBrush" : "NerdyChipBorderBrush");
                BorderThickness = new Thickness(1);
                Foreground = B(on ? "NerdyOnAccentBrush" : "NerdyTextPrimaryBrush");
                IconForeground = Foreground;
                IconBackground = Background;
                break;
            case ChoiceLook.ColorChip:
                Background = on ? ColorMix.Brush(_theme.Tint(color, 14)) : B("NerdyCardBackgroundBrush");
                BorderBrush = on ? ColorMix.Brush(color) : B("NerdyChipBorderBrush");
                BorderThickness = new Thickness(1);
                Foreground = B("NerdyTextPrimaryBrush");
                IconForeground = ColorMix.Brush(color);
                IconBackground = Background;
                break;
            case ChoiceLook.Segment:
                Background = B(on ? "NerdySegmentSelectedBrush" : "NerdyTransparentBrush");
                BorderBrush = B(on ? "NerdyCardBorderBrush" : "NerdyTransparentBrush");
                BorderThickness = new Thickness(on ? 1 : 0);
                Foreground = B(on ? "NerdyTextPrimaryBrush" : "NerdyTextSecondaryBrush");
                IconForeground = Foreground;
                IconBackground = Background;
                break;
        }

        if (!IsEnabled) Foreground = B("NerdyTextTertiaryBrush");
    }
}
