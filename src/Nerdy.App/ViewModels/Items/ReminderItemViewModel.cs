using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Services;
using Nerdy.App.Services.Classes;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Windows.UI.Text;

namespace Nerdy.App.ViewModels.Items;

/// <summary>A reminder or a class assignment, shown with a round check, its due time and topic/class colour.</summary>
public sealed partial class ReminderItemViewModel : ObservableObject
{
    private readonly ReminderDto? _reminder;
    private readonly AssignmentDto? _assignment;
    private readonly DataService _data;
    private readonly ClassesService _classes;
    private readonly ThemeService _theme;
    private readonly ToastService _toasts;

    private ReminderItemViewModel(DataService data, ClassesService classes, ThemeService theme, ToastService toasts, DateOnly today,
        ReminderDto? reminder, AssignmentDto? assignment)
    {
        _data = data;
        _classes = classes;
        _theme = theme;
        _toasts = toasts;
        _reminder = reminder;
        _assignment = assignment;

        if (reminder is not null)
        {
            Title = reminder.Title;
            IsDone = reminder.IsDone;
            IsStarred = reminder.IsStarred;
            DueAt = reminder.DueAt?.LocalDateTime;
            DueText = reminder.DueAt is { } d ? Fmt.Due(d.LocalDateTime, reminder.HasDueTime, today) : "No date";
            ContextText = data.Topic(reminder.TopicId)?.Name ?? "General";
            ColorHex = data.Topic(reminder.TopicId)?.ColorHex ?? ColorMix.ToHex(theme.Accent);
            Notes = reminder.Notes;
        }
        else if (assignment is not null)
        {
            Title = assignment.Title;
            IsDone = assignment.IsDone;
            DueAt = assignment.DueAt.LocalDateTime;
            DueText = Fmt.Due(assignment.DueAt.LocalDateTime, true, today);
            ContextText = assignment.ClassroomName;
            ColorHex = assignment.ClassColorHex;
            Notes = assignment.Description;
        }
        IsOverdue = !IsDone && DueAt is { } due && (reminder?.HasDueTime == false ? DateOnly.FromDateTime(due) < today : due < DateTime.Now);
        Refresh();
    }

    public static ReminderItemViewModel For(ReminderDto r, DataService data, ClassesService classes, ThemeService theme, ToastService toasts, DateOnly today) =>
        new(data, classes, theme, toasts, today, r, null);

    public static ReminderItemViewModel For(AssignmentDto a, DataService data, ClassesService classes, ThemeService theme, ToastService toasts, DateOnly today) =>
        new(data, classes, theme, toasts, today, null, a);

    public Guid Id => _reminder?.Id ?? _assignment!.Id;
    public ReminderDto? Reminder => _reminder;
    public bool IsAssignment => _assignment is not null;
    public bool IsPersonal => _reminder is not null;
    public string Title { get; } = "";
    public string DueText { get; } = "";
    public string ContextText { get; } = "";
    public string? Notes { get; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public string ColorHex { get; } = "#5B5BD6";
    public DateTime? DueAt { get; }
    public bool IsOverdue { get; }
    public Symbol SourceIcon => IsAssignment ? Symbol.HatGraduation : Symbol.Circle;
    public string MetaText => IsAssignment ? $"{DueText} · {ContextText} assignment" : DueText;

    [ObservableProperty] public partial bool IsDone { get; private set; }
    [ObservableProperty] public partial bool IsStarred { get; private set; }

    [ObservableProperty] public partial Brush CheckBackground { get; private set; } = null!;
    [ObservableProperty] public partial Brush CheckBorder { get; private set; } = null!;
    [ObservableProperty] public partial Brush TitleBrush { get; private set; } = null!;
    [ObservableProperty] public partial Brush DueBrush { get; private set; } = null!;
    [ObservableProperty] public partial Brush StarBrush { get; private set; } = null!;
    [ObservableProperty] public partial TextDecorations Decorations { get; private set; }
    public Brush DotBrush => ColorMix.Brush(ColorHex);
    public IconVariant StarVariant => IsStarred ? IconVariant.Filled : IconVariant.Regular;

    [RelayCommand]
    private async Task ToggleDoneAsync()
    {
        var done = !IsDone;
        IsDone = done;
        Refresh();
        if (_reminder is not null)
        {
            _data.Save(_reminder with { IsDone = done, CompletedAt = done ? DateTimeOffset.Now : null });
            if (done) _toasts.Show("Reminder completed", "Undo", () => _data.Save(_reminder with { IsDone = false, CompletedAt = null }), ToastKind.Success);
        }
        else if (_assignment is not null)
        {
            try { await _classes.SetDoneAsync(_assignment.Id, done); }
            catch (Exception ex)
            {
                IsDone = !done;
                Refresh();
                _toasts.Error(ex);
            }
        }
    }

    [RelayCommand]
    private void ToggleStar()
    {
        if (_reminder is null) return;
        IsStarred = !IsStarred;
        OnPropertyChanged(nameof(StarVariant));
        Refresh();
        _data.Save(_reminder with { IsStarred = IsStarred });
    }

    [RelayCommand]
    private void Delete()
    {
        if (_reminder is null) return;
        _data.Delete(_reminder);
        _toasts.Show("Reminder deleted", "Undo", () => _data.Save(_reminder with { IsDeleted = false }));
    }

    private void Refresh()
    {
        CheckBackground = _theme.Brush(IsDone ? "NerdyAccentBrush" : "NerdyTransparentBrush");
        CheckBorder = _theme.Brush(IsDone ? "NerdyAccentBrush" : "NerdyControlBorderStrongBrush");
        TitleBrush = _theme.Brush(IsDone ? "NerdyTextSecondaryBrush" : "NerdyTextPrimaryBrush");
        DueBrush = _theme.Brush(IsOverdue ? "NerdyDangerTextBrush" : "NerdyTextSecondaryBrush");
        StarBrush = _theme.Brush(IsStarred ? "NerdyStarBrush" : "NerdyTextTertiaryBrush");
        Decorations = IsDone ? TextDecorations.Strikethrough : TextDecorations.None;
    }
}
