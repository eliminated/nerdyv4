using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Classes;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;
using Windows.ApplicationModel.DataTransfer;

namespace Nerdy.App.ViewModels;

public sealed partial class ClassCardViewModel : ObservableObject
{
    private readonly ThemeService _theme;
    private readonly Action<ClassCardViewModel> _select;

    public ClassCardViewModel(ClassroomDto dto, ThemeService theme, Action<ClassCardViewModel> select)
    {
        Dto = dto;
        _theme = theme;
        _select = select;
        var color = ColorMix.Parse(dto.ColorHex);
        TileBackground = ColorMix.Brush(theme.Tint(color, 18));
        TileForeground = ColorMix.Brush(theme.Ink(color));
        Refresh();
    }

    public ClassroomDto Dto { get; }
    public Guid Id => Dto.Id;
    public string Name => Dto.Name;
    public string Initial => Dto.Name.Trim() is { Length: > 0 } n ? n[..1].ToUpperInvariant() : "?";
    public string Subtitle => Dto.Subject ?? (Dto.MyRole == ClassRole.Teacher ? "No subject" : $"Taught by {Dto.TeacherName}");
    public string Meta => Dto.MyRole == ClassRole.Teacher
        ? $"You teach · {Dto.StudentCount} student{(Dto.StudentCount == 1 ? "" : "s")}"
        : Dto.Subject is null ? "Student" : $"Taught by {Dto.TeacherName}";
    public Brush TileBackground { get; }
    public Brush TileForeground { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial Brush Background { get; private set; } = null!;
    [ObservableProperty] public partial Brush BorderBrush { get; private set; } = null!;

    partial void OnIsSelectedChanged(bool value) => Refresh();

    private void Refresh()
    {
        Background = _theme.Brush(IsSelected ? "NerdyAccentSoftBrush" : "NerdyCardBackgroundBrush");
        BorderBrush = _theme.Brush(IsSelected ? "NerdyAccentSoftBorderBrush" : "NerdyCardBorderBrush");
    }

    [RelayCommand]
    private void Select() => _select(this);
}

public sealed partial class AssignmentRowViewModel : ObservableObject
{
    private readonly ThemeService _theme;
    private readonly DateTime _now;
    private readonly Func<AssignmentRowViewModel, Task> _toggle;
    private readonly Func<AssignmentRowViewModel, Task> _edit;
    private readonly Func<AssignmentRowViewModel, Task> _delete;

    public AssignmentRowViewModel(AssignmentDto dto, bool teacher, ThemeService theme, DateTime now, DateOnly today,
        Func<AssignmentRowViewModel, Task> toggle, Func<AssignmentRowViewModel, Task> edit, Func<AssignmentRowViewModel, Task> delete)
    {
        Dto = dto;
        IsTeacher = teacher;
        _theme = theme;
        _now = now;
        _toggle = toggle;
        _edit = edit;
        _delete = delete;
        var due = dto.DueAt.LocalDateTime;
        DueText = $"Due {Fmt.RelativeDay(DateOnly.FromDateTime(due), today)} · {Fmt.Clock(due)}";
        ProgressText = $"{dto.CompletedCount} of {dto.StudentCount} done";
        ProgressPercent = dto.StudentCount == 0 ? 0 : dto.CompletedCount * 100.0 / dto.StudentCount;
        IsDone = dto.IsDone;
    }

    public AssignmentDto Dto { get; }
    public bool IsTeacher { get; }
    public bool IsStudent => !IsTeacher;
    public string Title => Dto.Title;
    public string? Description => Dto.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Dto.Description);
    public string DueText { get; }
    public string ProgressText { get; }
    public double ProgressPercent { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOverdue), nameof(DueBrush), nameof(CheckBackground), nameof(CheckBorder), nameof(TitleOpacity))]
    public partial bool IsDone { get; set; }

    public bool IsOverdue => Dto.DueAt.LocalDateTime < _now && (IsTeacher || !IsDone);
    public Brush DueBrush => _theme.Brush(IsOverdue && !IsTeacher ? "NerdyDangerTextBrush" : "NerdyTextSecondaryBrush");
    public Brush CheckBackground => _theme.Brush(IsDone ? "NerdyAccentBrush" : "NerdyTransparentBrush");
    public Brush CheckBorder => _theme.Brush(IsDone ? "NerdyAccentBrush" : "NerdyControlBorderStrongBrush");
    public double TitleOpacity => IsDone && IsStudent ? 0.55 : 1;
    public string CheckName => IsDone ? $"Mark {Title} not done" : $"Mark {Title} done";

    [RelayCommand] private Task ToggleDoneAsync() => _toggle(this);
    [RelayCommand] private Task EditAsync() => _edit(this);
    [RelayCommand] private Task DeleteAsync() => _delete(this);
}

public sealed partial class MemberRowViewModel(ClassMemberDto member, bool canRemove, Func<MemberRowViewModel, Task> remove)
{
    public ClassMemberDto Member { get; } = member;
    public string Initials { get; } = UserInitials.From(member.DisplayName);
    public string Name { get; } = member.DisplayName;
    public string Detail { get; } = member.Email ?? (member.Role == ClassRole.Teacher ? "Teacher" : "Student");
    public string Joined { get; } = member.Role == ClassRole.Teacher ? "Teacher" : $"Joined {member.JoinedAt.LocalDateTime:MMM d}";
    public bool IsStudent { get; } = member.Role == ClassRole.Student;
    public string SharingText { get; } = member.SharesFocus ? "Shares focus" : "Not sharing";
    public bool SharesFocus { get; } = member.SharesFocus;
    public bool CanRemove { get; } = canRemove;

    [RelayCommand] private Task RemoveAsync() => remove(this);
}

public sealed record SparkBar(double Height, Brush Fill);

public sealed record ProgressRow(string Initials, string Name, bool Shares, string FocusSummary,
    string Assignments, double AssignmentPercent, IReadOnlyList<SparkBar> Bars)
{
    public bool NotShared => !Shares;
}

/// <summary>
/// Classes: teachers create classes, share a join code, assign work and (with each student's opt-in) see focus
/// progress. Students join with a code, choose whether to share their focus stats, and tick off assignments.
/// Server-authoritative; the class list and a student's assignments are cached for offline viewing.
/// </summary>
public sealed partial class ClassesViewModel : ObservableObject, INavigationAware
{
    public const string AssignmentsTab = "assignments", StudentsTab = "students", ProgressTab = "progress";
    private const double SparkHeight = 28;

    private readonly ClassesService _classes;
    private readonly AuthService _auth;
    private readonly ThemeService _theme;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly NavigationService _navigation;
    private readonly IClock _clock;
    private readonly ILogger<ClassesViewModel> _logger;
    private Guid? _selectedId;
    private int _detailVersion;
    private int _progressDays = 7;
    private bool _settingShare;

    public ClassesViewModel(ClassesService classes, AuthService auth, ThemeService theme, DialogService dialogs, ToastService toasts,
        NavigationService navigation, IClock clock, ILogger<ClassesViewModel> logger)
    {
        _classes = classes;
        _auth = auth;
        _theme = theme;
        _dialogs = dialogs;
        _toasts = toasts;
        _navigation = navigation;
        _clock = clock;
        _logger = logger;
    }

    public ObservableCollection<ClassCardViewModel> Cards { get; } = [];
    public ObservableCollection<ChoiceItem> DetailTabs { get; } = [];
    public ObservableCollection<AssignmentRowViewModel> Assignments { get; } = [];
    public ObservableCollection<MemberRowViewModel> Members { get; } = [];
    public ObservableCollection<ProgressRow> Progress { get; } = [];
    public ObservableCollection<ChoiceItem> ProgressRanges { get; } = [];

    public bool CanCreate => _auth.User?.Role == UserRole.Teacher;
    public bool CannotCreate => !CanCreate;

    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial bool IsOffline { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoClasses))] public partial bool HasClasses { get; private set; }
    public bool NoClasses => !HasClasses;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanJoin))] public partial string JoinCodeInput { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanJoin))] public partial bool IsBusy { get; private set; }
    public bool CanJoin => !IsBusy && JoinCodeInput.Trim().Length >= 4;

    // ----- Selected class -----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsTeacherView), nameof(IsStudentView), nameof(SelectedName), nameof(SelectedSubtitle),
        nameof(SelectedInitial), nameof(SelectedTileBackground), nameof(SelectedTileForeground), nameof(JoinCode), nameof(SharingTitle))]
    public partial ClassCardViewModel? Selected { get; private set; }

    public bool HasSelection => Selected is not null;
    public bool IsTeacherView => Selected?.Dto.MyRole == ClassRole.Teacher;
    public bool IsStudentView => Selected?.Dto.MyRole == ClassRole.Student;
    public string SelectedName => Selected?.Name ?? "";
    public string SelectedSubtitle => Selected is not { } s ? "" : string.Join(" · ", new[] { s.Dto.Subject, $"Teacher: {s.Dto.TeacherName}" }.Where(x => x is not null));
    public string SelectedInitial => Selected?.Initial ?? "";
    public Brush? SelectedTileBackground => Selected?.TileBackground;
    public Brush? SelectedTileForeground => Selected?.TileForeground;
    public string JoinCode => Selected?.Dto.JoinCode ?? "";
    public string SharingTitle => Selected is { } s ? $"Share my focus stats with {s.Dto.TeacherName}" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssignmentsTab), nameof(IsStudentsTab), nameof(IsProgressTab))]
    public partial string DetailTab { get; private set; } = AssignmentsTab;

    public bool IsAssignmentsTab => DetailTab == AssignmentsTab || IsStudentView;
    public bool IsStudentsTab => DetailTab == StudentsTab && IsTeacherView;
    public bool IsProgressTab => DetailTab == ProgressTab && IsTeacherView;

    [ObservableProperty] public partial bool IsDetailLoading { get; private set; }
    [ObservableProperty] public partial bool SharesFocus { get; set; }
    [ObservableProperty] public partial bool NoAssignments { get; private set; }
    [ObservableProperty] public partial bool NoStudents { get; private set; }
    [ObservableProperty] public partial string ProgressSummary { get; private set; } = "";

    public async void OnNavigatedTo(object? parameter)
    {
        _classes.Changed += OnClassesChanged;
        _theme.Changed += OnThemeChanged;
        if (parameter is Guid id) _selectedId = id;
        RebuildCards();
        IsLoading = true;
        IsOffline = !await _classes.RefreshAsync();
        IsLoading = false;
    }

    public void OnNavigatedFrom()
    {
        _classes.Changed -= OnClassesChanged;
        _theme.Changed -= OnThemeChanged;
    }

    private void OnClassesChanged(object? sender, EventArgs e) => RebuildCards();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RebuildCards();
        RebuildTabs();
    }

    private void RebuildCards()
    {
        Cards.Clear();
        foreach (var c in _classes.Classes.Where(c => !c.IsArchived)
                     .OrderBy(c => c.MyRole == ClassRole.Teacher ? 0 : 1).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            Cards.Add(new ClassCardViewModel(c, _theme, card => Select(card.Id)));
        HasClasses = Cards.Count > 0;
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CannotCreate));

        var target = Cards.FirstOrDefault(c => c.Id == _selectedId) ?? Cards.FirstOrDefault();
        var changed = target?.Id != Selected?.Id;
        foreach (var c in Cards) c.IsSelected = c == target;
        Selected = target;
        _selectedId = target?.Id;
        if (target is null)
        {
            ClearDetail();
            return;
        }
        if (changed) _ = LoadDetailAsync();
        else if (IsStudentView) BuildStudentAssignments(); // cache refreshed (e.g. ticked off on Home)
    }

    private void Select(Guid id)
    {
        if (id == _selectedId) return;
        _selectedId = id;
        DetailTab = AssignmentsTab;
        RebuildCards();
    }

    private void ClearDetail()
    {
        Assignments.Clear();
        Members.Clear();
        Progress.Clear();
        DetailTabs.Clear();
    }

    private void RebuildTabs()
    {
        DetailTabs.Clear();
        if (!IsTeacherView) return;
        foreach (var (id, label) in new[] { (AssignmentsTab, "Assignments"), (StudentsTab, "Students"), (ProgressTab, "Progress") })
            DetailTabs.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, id, PickTab, selected: id == DetailTab));
        ProgressRanges.Clear();
        foreach (var (days, label) in new[] { (7, "7 days"), (30, "30 days") })
            ProgressRanges.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, days, PickRange, selected: days == _progressDays));
    }

    private void PickTab(ChoiceItem item)
    {
        DetailTab = (string)item.Value!;
        foreach (var t in DetailTabs) t.IsSelected = t == item;
    }

    private async void PickRange(ChoiceItem item)
    {
        _progressDays = (int)item.Value!;
        foreach (var r in ProgressRanges) r.IsSelected = r == item;
        if (Selected is { } s) await LoadProgressAsync(s.Id, ++_detailVersion);
    }

    private async Task LoadDetailAsync()
    {
        var version = ++_detailVersion;
        RebuildTabs();
        Assignments.Clear();
        Members.Clear();
        Progress.Clear();
        if (Selected is not { } card) return;

        if (card.Dto.MyRole == ClassRole.Student)
        {
            _settingShare = true;
            SharesFocus = card.Dto.SharesFocus;
            _settingShare = false;
            BuildStudentAssignments();
            return;
        }

        IsDetailLoading = true;
        try
        {
            var assignments = _classes.AssignmentsAsync(card.Id);
            var members = _classes.MembersAsync(card.Id);
            await Task.WhenAll(assignments, members);
            if (version != _detailVersion) return;
            BuildTeacherAssignments(assignments.Result);
            Members.Clear();
            foreach (var m in members.Result)
                Members.Add(new MemberRowViewModel(m, m.Role == ClassRole.Student, RemoveMemberAsync));
            NoStudents = !Members.Any(m => m.IsStudent);
            await LoadProgressAsync(card.Id, version);
        }
        catch (Exception ex) when (ex is ApiException or OfflineException)
        {
            if (version != _detailVersion) return;
            _logger.LogInformation("Couldn't load class details: {Message}", ex.Message);
            _toasts.Show(ex is OfflineException ? "You're offline. Class details need a connection." : ((ApiException)ex).UserMessage, ToastKind.Warning);
        }
        finally
        {
            if (version == _detailVersion) IsDetailLoading = false;
        }
    }

    private void BuildStudentAssignments()
    {
        if (Selected is not { } card) return;
        var now = _clock.LocalNow;
        Assignments.Clear();
        foreach (var a in _classes.MyAssignments.Where(a => a.ClassroomId == card.Id).OrderBy(a => a.IsDone).ThenBy(a => a.DueAt))
            Assignments.Add(new AssignmentRowViewModel(a, false, _theme, now, _clock.Today, ToggleDoneAsync, _ => Task.CompletedTask, _ => Task.CompletedTask));
        NoAssignments = Assignments.Count == 0;
    }

    private void BuildTeacherAssignments(IEnumerable<AssignmentDto> list)
    {
        var now = _clock.LocalNow;
        Assignments.Clear();
        // Upcoming first (soonest due), then past ones (most recent first).
        foreach (var a in list.OrderBy(a => a.DueAt.LocalDateTime < now).ThenBy(a => a.DueAt.LocalDateTime < now ? -a.DueAt.Ticks : a.DueAt.Ticks))
            Assignments.Add(new AssignmentRowViewModel(a, true, _theme, now, _clock.Today, _ => Task.CompletedTask, EditAssignmentAsync, DeleteAssignmentAsync));
        NoAssignments = Assignments.Count == 0;
    }

    private async Task LoadProgressAsync(Guid classId, int version)
    {
        var today = _clock.Today;
        var from = new DateTimeOffset(today.AddDays(-(_progressDays - 1)).ToDateTime(TimeOnly.MinValue));
        var to = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue));
        try
        {
            var progress = await _classes.ProgressAsync(classId, from, to);
            if (version != _detailVersion) return;
            var max = Math.Max(1, progress.Students.Where(s => s.DailyMinutes is not null).SelectMany(s => s.DailyMinutes!).DefaultIfEmpty(0).Max());
            Progress.Clear();
            foreach (var s in progress.Students)
            {
                var bars = (s.DailyMinutes ?? []).Select(m => m > 0
                    ? new SparkBar(Math.Max(3, m / (double)max * SparkHeight), _theme.Brush("NerdyAccentBrush"))
                    : new SparkBar(2, _theme.Brush("NerdyTrackBrush"))).ToList();
                Progress.Add(new ProgressRow(UserInitials.From(s.DisplayName), s.DisplayName, s.SharesFocus,
                    FocusSummary(s),
                    $"{s.AssignmentsDone} of {s.AssignmentsTotal}",
                    s.AssignmentsTotal == 0 ? 0 : s.AssignmentsDone * 100.0 / s.AssignmentsTotal,
                    bars));
            }
            var sharing = progress.Students.Count(s => s.SharesFocus);
            ProgressSummary = progress.Students.Count == 0 ? ""
                : $"Sharing focus stats with you: {sharing} of {progress.Students.Count} student{(progress.Students.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex) when (ex is ApiException or OfflineException)
        {
            _logger.LogInformation("Couldn't load class progress: {Message}", ex.Message);
        }
    }

    private static string FocusSummary(StudentProgressDto s) => s switch
    {
        { SharesFocus: false } => "Focus stats not shared",
        { Sessions: null or 0 } => "No focus sessions in this period",
        _ => $"{Fmt.HoursMinutes(s.FocusMinutes ?? 0)} · {s.Sessions} session{(s.Sessions == 1 ? "" : "s")}" +
             (s.CompletedAsPlannedPercent is { } p ? $" · {p}% as planned" : "")
    };

    // ----- Joining and creating -----

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (!CanJoin) return;
        await RunAsync(async () =>
        {
            var joined = await _classes.JoinAsync(JoinCodeInput);
            JoinCodeInput = "";
            _selectedId = joined.Id;
            RebuildCards();
            _toasts.Show($"You joined {joined.Name}", ToastKind.Success);
        });
    }

    [RelayCommand]
    private async Task CreateClassAsync()
    {
        var editor = new ClassEditorViewModel(_theme, null, _classes.Classes.Select(c => c.ColorHex));
        if (await _dialogs.ShowAsync(new Views.Dialogs.ClassEditorDialog(editor)) != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            var created = await _classes.CreateAsync(editor.Name, editor.Subject, editor.SelectedColor);
            _selectedId = created.Id;
            RebuildCards();
            _toasts.Show($"{created.Name} is ready. Share the join code with your students.", ToastKind.Success);
        });
    }

    [RelayCommand]
    private void OpenAccountSettings() => _navigation.Navigate(NavSections.Settings, SettingsViewModel.AccountTab);

    // ----- Teacher actions -----

    [RelayCommand]
    private async Task EditClassAsync()
    {
        if (Selected is not { } card) return;
        var editor = new ClassEditorViewModel(_theme, card.Dto, []);
        if (await _dialogs.ShowAsync(new Views.Dialogs.ClassEditorDialog(editor)) != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        await RunAsync(() => _classes.UpdateAsync(card.Id, new UpdateClassroomRequest
        {
            Name = editor.Name.Trim(),
            Subject = editor.Subject.Trim(),
            ColorHex = editor.SelectedColor
        }));
    }

    [RelayCommand]
    private async Task DeleteClassAsync()
    {
        if (Selected is not { } card) return;
        if (!await _dialogs.ConfirmAsync($"Delete {card.Name}?",
                "Students lose access to the class and its assignments are deleted. This can't be undone.", "Delete class", destructive: true)) return;
        await RunAsync(async () =>
        {
            await _classes.DeleteAsync(card.Id);
            _toasts.Show($"{card.Name} was deleted", ToastKind.Info);
        });
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (JoinCode.Length == 0) return;
        var package = new DataPackage();
        package.SetText(JoinCode);
        Clipboard.SetContent(package);
        _toasts.Show("Join code copied", ToastKind.Success);
    }

    [RelayCommand]
    private async Task NewCodeAsync()
    {
        if (Selected is not { } card) return;
        if (!await _dialogs.ConfirmAsync("Make a new join code?",
                "The current code stops working. Students who already joined stay in the class.", "New code")) return;
        await RunAsync(() => _classes.RegenerateCodeAsync(card.Id));
    }

    [RelayCommand]
    private async Task NewAssignmentAsync()
    {
        if (Selected is not { } card) return;
        var editor = new AssignmentEditorViewModel(null, _clock.LocalNow);
        if (await _dialogs.ShowAsync(new Views.Dialogs.AssignmentEditorDialog(editor)) != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            await _classes.CreateAssignmentAsync(card.Id, editor.Title, editor.Description, editor.DueAt);
            await ReloadTeacherAssignmentsAsync(card.Id);
            _toasts.Show("Assignment added", ToastKind.Success);
        });
    }

    private async Task EditAssignmentAsync(AssignmentRowViewModel row)
    {
        var editor = new AssignmentEditorViewModel(row.Dto, _clock.LocalNow);
        if (await _dialogs.ShowAsync(new Views.Dialogs.AssignmentEditorDialog(editor)) != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
        await RunAsync(async () =>
        {
            await _classes.UpdateAssignmentAsync(row.Dto.Id, new UpdateAssignmentRequest
            {
                Title = editor.Title.Trim(),
                Description = editor.Description.Trim(),
                DueAt = editor.DueAt
            });
            await ReloadTeacherAssignmentsAsync(row.Dto.ClassroomId);
        });
    }

    private async Task DeleteAssignmentAsync(AssignmentRowViewModel row)
    {
        if (!await _dialogs.ConfirmAsync($"Delete \"{row.Title}\"?", "It disappears for every student in the class.", "Delete", destructive: true)) return;
        await RunAsync(async () =>
        {
            await _classes.DeleteAssignmentAsync(row.Dto.Id);
            await ReloadTeacherAssignmentsAsync(row.Dto.ClassroomId);
        });
    }

    private async Task ReloadTeacherAssignmentsAsync(Guid classId)
    {
        var list = await _classes.AssignmentsAsync(classId);
        if (Selected?.Id == classId) BuildTeacherAssignments(list);
    }

    private async Task RemoveMemberAsync(MemberRowViewModel row)
    {
        if (Selected is not { } card) return;
        if (!await _dialogs.ConfirmAsync($"Remove {row.Name} from {card.Name}?",
                "They can join again with the class code. Their assignment check-offs for this class are removed.", "Remove", destructive: true)) return;
        await RunAsync(async () =>
        {
            await _classes.RemoveMemberAsync(card.Id, row.Member.UserId);
            Members.Remove(row);
            NoStudents = !Members.Any(m => m.IsStudent);
        });
    }

    // ----- Student actions -----

    private async Task ToggleDoneAsync(AssignmentRowViewModel row)
    {
        var done = !row.IsDone;
        row.IsDone = done;
        try
        {
            await _classes.SetDoneAsync(row.Dto.Id, done); // raises Changed → list rebuilt from the cache
        }
        catch (Exception ex) when (ex is ApiException or OfflineException)
        {
            row.IsDone = !done;
            _toasts.Show(ex is OfflineException ? "You're offline. Try again when you're connected." : ((ApiException)ex).UserMessage, ToastKind.Warning);
        }
    }

    partial void OnSharesFocusChanged(bool value)
    {
        if (_settingShare || Selected is not { } card) return;
        _ = SetSharingAsync(card, value);
    }

    private async Task SetSharingAsync(ClassCardViewModel card, bool share)
    {
        try
        {
            await _classes.SetSharingAsync(card.Id, share);
            _toasts.Show(share ? $"{card.Dto.TeacherName} can now see your focus stats" : "Your focus stats are private again", ToastKind.Success);
        }
        catch (Exception ex) when (ex is ApiException or OfflineException)
        {
            _settingShare = true;
            SharesFocus = !share;
            _settingShare = false;
            _toasts.Show(ex is OfflineException ? "You're offline. Try again when you're connected." : ((ApiException)ex).UserMessage, ToastKind.Warning);
        }
    }

    [RelayCommand]
    private async Task LeaveClassAsync()
    {
        if (Selected is not { } card || _auth.User is not { } me) return;
        if (!await _dialogs.ConfirmAsync($"Leave {card.Name}?",
                "Its assignments disappear from your list. You can rejoin later with the class code.", "Leave class", destructive: true)) return;
        await RunAsync(async () =>
        {
            await _classes.RemoveMemberAsync(card.Id, me.Id);
            _toasts.Show($"You left {card.Name}", ToastKind.Info);
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            _toasts.Show(ex.UserMessage, ToastKind.Error);
        }
        catch (OfflineException)
        {
            _toasts.Show("You're offline. Classes need a connection for this.", ToastKind.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
