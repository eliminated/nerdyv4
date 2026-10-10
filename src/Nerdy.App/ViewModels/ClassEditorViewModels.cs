using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nerdy.App.Services.Theming;
using Nerdy.Contracts;
using Nerdy.Core.Topics;

namespace Nerdy.App.ViewModels;

/// <summary>Inputs for creating or editing a class; the caller sends them to the server.</summary>
public sealed partial class ClassEditorViewModel : ObservableObject
{
    private readonly ThemeService _theme;

    public ClassEditorViewModel(ThemeService theme, ClassroomDto? existing, IEnumerable<string> usedColors)
    {
        _theme = theme;
        IsNew = existing is null;
        Name = existing?.Name ?? "";
        Subject = existing?.Subject ?? "";
        SelectedColor = existing?.ColorHex ?? TopicPalette.Next(usedColors);
        RebuildSwatches();
    }

    public bool IsNew { get; }
    public string DialogTitle => IsNew ? "New class" : "Edit class";
    public string PrimaryText => IsNew ? "Create class" : "Save";
    public ObservableCollection<SwatchItem> Swatches { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial string Name { get; set; }
    [ObservableProperty] public partial string Subject { get; set; }
    [ObservableProperty] public partial string SelectedColor { get; set; }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    private void RebuildSwatches()
    {
        Swatches.Clear();
        foreach (var hex in TopicPalette.Colors)
            Swatches.Add(new SwatchItem(hex, ColorMix.Brush(hex),
                hex.Equals(SelectedColor, StringComparison.OrdinalIgnoreCase) ? ColorMix.Brush(hex) : _theme.Brush("NerdyTransparentBrush"),
                h => { SelectedColor = h; RebuildSwatches(); }));
    }
}

/// <summary>Inputs for creating or editing an assignment; the caller sends them to the server.</summary>
public sealed partial class AssignmentEditorViewModel : ObservableObject
{
    public AssignmentEditorViewModel(AssignmentDto? existing, DateTime now)
    {
        IsNew = existing is null;
        Title = existing?.Title ?? "";
        Description = existing?.Description ?? "";
        // New assignments default to the end of the day a week from now.
        var due = existing?.DueAt.LocalDateTime ?? now.Date.AddDays(7).AddHours(23).AddMinutes(59);
        DueDate = new DateTimeOffset(due.Date);
        DueTime = due.TimeOfDay;
    }

    public bool IsNew { get; }
    public string DialogTitle => IsNew ? "New assignment" : "Edit assignment";
    public string PrimaryText => IsNew ? "Assign" : "Save";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial string Title { get; set; }
    [ObservableProperty] public partial string Description { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial DateTimeOffset? DueDate { get; set; }
    [ObservableProperty] public partial TimeSpan DueTime { get; set; }

    public bool CanSave => !string.IsNullOrWhiteSpace(Title) && DueDate is not null;

    public DateTimeOffset DueAt => new(DueDate!.Value.Date + DueTime, TimeZoneInfo.Local.GetUtcOffset(DueDate.Value.Date + DueTime));
}
