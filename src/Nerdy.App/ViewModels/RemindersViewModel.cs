using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Nerdy.App.Services;
using Nerdy.App.Services.Classes;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.App.ViewModels.Items;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;

namespace Nerdy.App.ViewModels;

public enum ReminderFilter { All, Today, Upcoming, Done }

public sealed class ReminderGroup(string label, IReadOnlyList<ReminderItemViewModel> items)
{
    public string Header { get; } = $"{label} · {items.Count}";
    public IReadOnlyList<ReminderItemViewModel> Items { get; } = items;
}

public sealed partial class RemindersViewModel : ObservableObject, INavigationAware
{
    private readonly DataService _data;
    private readonly ClassesService _classes;
    private readonly ThemeService _theme;
    private readonly ToastService _toasts;
    private readonly DialogService _dialogs;
    private readonly PreferencesService _preferences;
    private readonly IClock _clock;
    private ReminderFilter _filter = ReminderFilter.All;

    public RemindersViewModel(DataService data, ClassesService classes, ThemeService theme, ToastService toasts, DialogService dialogs,
        PreferencesService preferences, IClock clock)
    {
        _data = data;
        _classes = classes;
        _theme = theme;
        _toasts = toasts;
        _dialogs = dialogs;
        _preferences = preferences;
        _clock = clock;
        NewDue = DueOptions[0];
    }

    /// <summary>Quick-add due choices (null = no date).</summary>
    public IReadOnlyList<Option<int?>> DueOptions { get; } =
        [new("Today", 0), new("Tomorrow", 1), new("In 3 days", 3), new("Next week", 7), new("No date", null)];

    public ObservableCollection<ChoiceItem> Filters { get; } = [];
    public ObservableCollection<ReminderGroup> Groups { get; } = [];

    [ObservableProperty] public partial string NewTitle { get; set; } = "";
    [ObservableProperty] public partial Option<int?> NewDue { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    [ObservableProperty] public partial string EmptyText { get; private set; } = "";
    [ObservableProperty] public partial Guid? HighlightId { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is Guid id) HighlightId = id;
        _data.Changed += OnDataChanged;
        _classes.Changed += OnClassesChanged;
        _theme.Changed += OnClassesChanged;
        Refresh();
    }

    public void OnNavigatedFrom()
    {
        _data.Changed -= OnDataChanged;
        _classes.Changed -= OnClassesChanged;
        _theme.Changed -= OnClassesChanged;
    }

    [RelayCommand]
    private void Add()
    {
        var title = NewTitle.Trim();
        if (title.Length == 0) return;
        DateTimeOffset? due = NewDue.Value is { } days ? new DateTimeOffset(_clock.Today.AddDays(days).ToDateTime(TimeOnly.MinValue)) : null;
        _data.Save(new ReminderDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, Title = title, DueAt = due });
        NewTitle = "";
        _toasts.Show("Reminder added", ToastKind.Success);
    }

    public async Task EditAsync(ReminderItemViewModel item)
    {
        if (item.Reminder is not { } reminder) return;
        var vm = new ReminderEditorViewModel(_data, _theme, _clock, _preferences.Current.StudyDayStartHour, reminder);
        await _dialogs.ShowAsync(new Views.Dialogs.ReminderEditorDialog(vm));
    }

    private void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.Affects(DataKinds.Reminders | DataKinds.Subjects | DataKinds.Topics)) Refresh();
    }

    private void OnClassesChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        Filters.Clear();
        foreach (var (f, label) in new[] { (ReminderFilter.All, "All"), (ReminderFilter.Today, "Today"), (ReminderFilter.Upcoming, "Upcoming"), (ReminderFilter.Done, "Completed") })
            Filters.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, f, c => { _filter = (ReminderFilter)c.Value!; Refresh(); }, selected: f == _filter));

        var today = _clock.Today;
        var all = _data.Reminders.Select(r => ReminderItemViewModel.For(r, _data, _classes, _theme, _toasts, today))
            .Concat(_classes.MyAssignments.Select(a => ReminderItemViewModel.For(a, _data, _classes, _theme, _toasts, today)))
            .ToList();

        static DateOnly? Day(ReminderItemViewModel r) => r.DueAt is { } d ? DateOnly.FromDateTime(d) : null;
        var open = all.Where(r => !r.IsDone).ToList();
        var overdue = open.Where(r => r.IsOverdue && Day(r) < today).OrderBy(r => r.DueAt).ToList();
        var dueToday = open.Where(r => Day(r) == today).OrderByDescending(r => r.IsStarred).ThenBy(r => r.DueAt).ToList();
        var upcoming = open.Where(r => Day(r) > today).OrderBy(r => r.DueAt).ToList();
        var undated = open.Where(r => r.DueAt is null).OrderByDescending(r => r.IsStarred).ThenBy(r => r.Title).ToList();
        var done = all.Where(r => r.IsDone).OrderByDescending(r => r.DueAt).Take(50).ToList();

        Groups.Clear();
        void Add(string label, List<ReminderItemViewModel> items) { if (items.Count > 0) Groups.Add(new ReminderGroup(label, items)); }
        if (_filter is ReminderFilter.All or ReminderFilter.Today) { Add("Overdue", overdue); Add("Today", dueToday); }
        if (_filter is ReminderFilter.All or ReminderFilter.Upcoming) { Add("Upcoming", upcoming); Add("No date", undated); }
        if (_filter is ReminderFilter.All or ReminderFilter.Done) Add("Completed", done);

        IsEmpty = Groups.Count == 0;
        EmptyText = _filter switch
        {
            ReminderFilter.Today => "Nothing due today.",
            ReminderFilter.Upcoming => "Nothing coming up. Add a reminder above.",
            ReminderFilter.Done => "Completed reminders show up here.",
            _ => "No reminders yet. Add one above."
        };
    }
}

/// <summary>
/// Edit one personal reminder. The due time has quick buttons (In 1 hour / This evening / Tomorrow); Custom shows the
/// date and time pickers.
/// </summary>
public sealed partial class ReminderEditorViewModel : ObservableObject
{
    private enum DuePick { InHour, Evening, Tomorrow, Custom }

    private const int EveningHour = 19;

    private readonly DataService _data;
    private readonly IClock _clock;
    private readonly ReminderDto _original;
    private readonly int _morningHour;
    private readonly bool _ready;

    public ReminderEditorViewModel(DataService data, ThemeService theme, IClock clock, int morningHour, ReminderDto reminder)
    {
        _data = data;
        _clock = clock;
        _original = reminder;
        _morningHour = morningHour;
        Link = new StudyPicker(data, reminder.SubjectId, reminder.TopicId);
        Title = reminder.Title;
        Notes = reminder.Notes ?? "";
        HasDate = reminder.DueAt is not null;
        var due = reminder.DueAt?.LocalDateTime ?? DateTime.Today;
        Date = new DateTimeOffset(due.Date);
        HasTime = reminder.HasDueTime;
        Time = reminder.HasDueTime ? due.TimeOfDay : new TimeSpan(17, 0, 0);
        IsStarred = reminder.IsStarred;

        var now = clock.LocalNow;
        DueChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "In 1 hour", DuePick.InHour, PickDue));
        DueChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "This evening", DuePick.Evening, PickDue)
        {
            IsEnabled = now.Hour < EveningHour,
            ToolTip = now.Hour < EveningHour ? Fmt.Clock(now.Date.AddHours(EveningHour)) : "It's already evening"
        });
        DueChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "Tomorrow", DuePick.Tomorrow, PickDue)
        {
            ToolTip = $"Tomorrow, {Fmt.Clock(now.Date.AddDays(1).AddHours(morningHour))}"
        });
        DueChips.Add(new ChoiceItem(theme, ChoiceLook.Chip, "Custom", DuePick.Custom, PickDue, Symbol.Timer));
        _ready = true;
        UpdateDueText();
    }

    public StudyPicker Link { get; }
    public ObservableCollection<ChoiceItem> DueChips { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial string Title { get; set; }
    [ObservableProperty] public partial string Notes { get; set; }
    [ObservableProperty] public partial bool HasDate { get; set; }
    [ObservableProperty] public partial DateTimeOffset? Date { get; set; }
    [ObservableProperty] public partial bool HasTime { get; set; }
    [ObservableProperty] public partial TimeSpan Time { get; set; }
    [ObservableProperty] public partial bool IsStarred { get; set; }
    [ObservableProperty] public partial bool IsCustom { get; private set; }
    [ObservableProperty] public partial string DueText { get; private set; } = "";

    public bool CanSave => !string.IsNullOrWhiteSpace(Title);

    partial void OnDateChanged(DateTimeOffset? value) => UpdateDueText();
    partial void OnHasTimeChanged(bool value) => UpdateDueText();
    partial void OnTimeChanged(TimeSpan value) => UpdateDueText();

    private void PickDue(ChoiceItem item)
    {
        foreach (var c in DueChips) c.IsSelected = c == item;
        var now = _clock.LocalNow;
        DateTime? at = (DuePick)item.Value! switch
        {
            DuePick.InHour => now.Date.AddMinutes((now.Hour * 60 + now.Minute + 60 + 4) / 5 * 5), // rounded up to 5 minutes
            DuePick.Evening => now.Date.AddHours(EveningHour),
            DuePick.Tomorrow => now.Date.AddDays(1).AddHours(_morningHour),
            _ => null
        };
        IsCustom = at is null;
        if (at is not { } t) return;
        Date = new DateTimeOffset(t.Date);
        HasTime = true;
        Time = t.TimeOfDay;
    }

    private void UpdateDueText()
    {
        if (!_ready) return;
        var day = Date?.Date ?? DateTime.Today;
        DueText = Fmt.Due(day + (HasTime ? Time : TimeSpan.Zero), HasTime, _clock.Today);
    }

    public void Save()
    {
        DateTimeOffset? due = HasDate && Date is { } d ? new DateTimeOffset(d.Date + (HasTime ? Time : TimeSpan.Zero)) : null;
        _data.Save(_original with
        {
            Title = Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            SubjectId = Link.SubjectId,
            TopicId = Link.TopicId,
            DueAt = due,
            HasDueTime = due is not null && HasTime,
            IsStarred = IsStarred
        });
    }

    public void Delete() => _data.Delete(_original);
}
