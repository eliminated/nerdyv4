using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Stats;
using Nerdy.Core.Subjects;
using Nerdy.Core.Time;
using Windows.UI.Text;

namespace Nerdy.App.ViewModels;

/// <summary>Something to study: a subject, optionally narrowed to one of its topics. Used as a navigation target.</summary>
public sealed record StudyLink(Guid SubjectId, Guid? TopicId = null);

/// <summary>Where the Notes page should open: a subject/topic/subtopic filter, optionally creating a note there.</summary>
public sealed record NotesTarget(Guid? SubjectId, Guid? TopicId, Guid? SubtopicId, bool CreateNew);

/// <summary>A subject in the left-hand list, expandable to its topics.</summary>
public sealed partial class SubjectNodeViewModel(SubjectDto subject, IReadOnlyList<TopicLeafViewModel> topics, bool expanded, bool selected,
    Brush rowBackground, Action<Guid> select) : ObservableObject
{
    public Guid Id { get; } = subject.Id;
    public string Name { get; } = subject.Name;
    public Brush Color { get; } = ColorMix.Brush(subject.ColorHex);
    public string TopicCount { get; } = topics.Count.ToString();
    public IReadOnlyList<TopicLeafViewModel> Topics { get; } = topics;
    public Brush RowBackground { get; } = rowBackground;
    public FontWeight Weight { get; } = selected ? FontWeights.SemiBold : FontWeights.Normal;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ChevronAngle))]
    public partial bool IsExpanded { get; set; } = expanded;

    public double ChevronAngle => IsExpanded ? 90 : 0;

    [RelayCommand]
    private void Select() => select(Id);

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>A topic under its subject in the left-hand list; selecting it jumps to its card.</summary>
public sealed partial class TopicLeafViewModel(TopicDto topic, Action<TopicDto> open)
{
    public Guid Id { get; } = topic.Id;
    public string Name { get; } = topic.Name;

    [RelayCommand]
    private void Open() => open(topic);
}

/// <summary>One topic of the selected subject, as a card with its subtopics.</summary>
public sealed partial class TopicCardViewModel : ObservableObject
{
    private readonly DataService _data;
    private readonly AgendaService _agenda;
    private readonly DialogService _dialogs;
    private readonly NavigationService _navigation;
    private readonly DispatcherQueueTimer _renameTimer;
    private readonly Action<TopicCardViewModel> _removed;
    private TopicDto _topic;

    public TopicCardViewModel(TopicDto topic, Brush color, DataService data, AgendaService agenda, DialogService dialogs,
        NavigationService navigation, Action<TopicCardViewModel> removed)
    {
        _topic = topic;
        _data = data;
        _agenda = agenda;
        _dialogs = dialogs;
        _navigation = navigation;
        _removed = removed;
        Color = color;
        Name = topic.Name;
        foreach (var s in data.SubtopicsOf(topic.Id)) Subtopics.Add(NewRow(s));
        _renameTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _renameTimer.Interval = TimeSpan.FromMilliseconds(500);
        _renameTimer.IsRepeating = false;
        _renameTimer.Tick += (_, _) =>
        {
            var name = Name.Trim();
            if (name.Length > 0 && name != _topic.Name) _topic = _data.Save(_topic with { Name = name });
        };
        RefreshProgress();
    }

    public Guid Id => _topic.Id;
    public Brush Color { get; }
    public ObservableCollection<SubtopicRowViewModel> Subtopics { get; } = [];

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial bool HasProgress { get; private set; }
    [ObservableProperty] public partial string PercentText { get; private set; } = "";
    [ObservableProperty] public partial string NotesText { get; private set; } = "";
    [ObservableProperty] public partial string NewSubtopic { get; set; } = "";

    partial void OnNameChanged(string value)
    {
        if (_renameTimer is null) return; // during construction
        _renameTimer.Stop();
        _renameTimer.Start();
    }

    /// <summary>Recomputes the topic's average and note count after a subtopic changed.</summary>
    public void RefreshProgress()
    {
        var progress = _agenda.TopicProgress(_topic.Id);
        HasProgress = progress is not null;
        Progress = progress ?? 0;
        PercentText = progress is { } p ? $"{p}%" : "";
        var notes = _data.Notes.Count(n => n.TopicId == _topic.Id);
        NotesText = $"{notes} note{(notes == 1 ? "" : "s")}";
    }

    [RelayCommand]
    private void AddSubtopic()
    {
        var name = NewSubtopic.Trim();
        if (name.Length == 0) return;
        var saved = _data.Save(new SubtopicDto
        {
            Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, TopicId = _topic.Id, Name = name, SortOrder = _data.SubtopicsOf(_topic.Id).Count,
        });
        Subtopics.Add(NewRow(saved));
        NewSubtopic = "";
        RefreshProgress();
    }

    [RelayCommand]
    private void OpenNotes() => _navigation.Navigate(NavSections.Notes, new NotesTarget(_topic.SubjectId, _topic.Id, null, CreateNew: false));

    [RelayCommand]
    private void NewNote() => _navigation.Navigate(NavSections.Notes, new NotesTarget(_topic.SubjectId, _topic.Id, null, CreateNew: true));

    [RelayCommand]
    private void Schedule() => _navigation.Navigate(NavSections.Schedule, new StudyLink(_topic.SubjectId, _topic.Id));

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var subject = _data.Subject(_topic.SubjectId)?.Name ?? "its subject";
        if (!await _dialogs.ConfirmAsync($"Delete {_topic.Name}?",
                $"Its subtopics are deleted too. Notes, reminders and calendar events stay in {subject}.", "Delete", destructive: true)) return;
        foreach (var s in _data.SubtopicsOf(_topic.Id)) _data.Delete(s);
        _data.Delete(_topic);
        _removed(this);
    }

    private SubtopicRowViewModel NewRow(SubtopicDto subtopic) =>
        new(subtopic, _data.Notes.Count(n => n.SubtopicId == subtopic.Id), Color, _data,
            id => _navigation.Navigate(NavSections.Notes, new NotesTarget(_topic.SubjectId, _topic.Id, id, CreateNew: false)),
            row => { Subtopics.Remove(row); RefreshProgress(); });
}

/// <summary>A subtopic row; progress and name edits are saved shortly after they stop changing.</summary>
public sealed partial class SubtopicRowViewModel : ObservableObject
{
    private readonly DataService _data;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly Action<Guid> _openNotes;
    private readonly Action<SubtopicRowViewModel> _deleted;
    private SubtopicDto _subtopic;

    public SubtopicRowViewModel(SubtopicDto subtopic, int notes, Brush color, DataService data, Action<Guid> openNotes,
        Action<SubtopicRowViewModel> deleted)
    {
        _subtopic = subtopic;
        _data = data;
        _openNotes = openNotes;
        _deleted = deleted;
        Name = subtopic.Name;
        Progress = subtopic.Progress;
        NotesText = $"{notes} note{(notes == 1 ? "" : "s")}";
        Color = color;
        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => _subtopic = _data.Save(_subtopic with
        {
            Progress = (int)Math.Round(Progress),
            Name = Name.Trim().Length == 0 ? _subtopic.Name : Name.Trim(),
        });
    }

    public string NotesText { get; }
    public Brush Color { get; }

    [ObservableProperty] public partial string Name { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PercentText))]
    public partial double Progress { get; set; }

    public string PercentText => $"{Math.Round(Progress)}%";

    partial void OnProgressChanged(double value) => ScheduleSave();
    partial void OnNameChanged(string value) => ScheduleSave();

    private void ScheduleSave()
    {
        if (_saveTimer is null) return; // during construction
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    [RelayCommand]
    private void OpenNotes() => _openNotes(_subtopic.Id);

    [RelayCommand]
    private void Delete()
    {
        _saveTimer.Stop();
        _data.Delete(_subtopic);
        _deleted(this);
    }
}

/// <summary>
/// The Subjects page: subjects on the left (each expandable to its topics), and the selected subject on the right with
/// one card per topic holding its subtopics and their progress.
/// </summary>
public sealed partial class SubjectsViewModel : ObservableObject, INavigationAware
{
    private readonly DataService _data;
    private readonly AgendaService _agenda;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly DialogService _dialogs;
    private readonly IClock _clock;
    private readonly HashSet<Guid> _expanded = [];
    private Guid? _selected;

    public SubjectsViewModel(DataService data, AgendaService agenda, ThemeService theme, NavigationService navigation,
        DialogService dialogs, IClock clock)
    {
        _data = data;
        _agenda = agenda;
        _theme = theme;
        _navigation = navigation;
        _dialogs = dialogs;
        _clock = clock;
    }

    public ObservableCollection<SubjectNodeViewModel> Tree { get; } = [];
    public ObservableCollection<TopicCardViewModel> Topics { get; } = [];

    /// <summary>Asks the page to scroll a topic's card into view.</summary>
    public event EventHandler<TopicCardViewModel>? TopicRequested;

    [ObservableProperty] public partial bool HasSubjects { get; private set; }
    [ObservableProperty] public partial bool HasTopics { get; private set; }
    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string Meta { get; private set; } = "";
    [ObservableProperty] public partial Brush? Color { get; private set; }
    [ObservableProperty] public partial Brush? Tint { get; private set; }
    [ObservableProperty] public partial string NextText { get; private set; } = "";
    [ObservableProperty] public partial string DueText { get; private set; } = "";
    [ObservableProperty] public partial bool HasDue { get; private set; }
    [ObservableProperty] public partial string FocusText { get; private set; } = "";
    [ObservableProperty] public partial string NoTopicsText { get; private set; } = "";
    [ObservableProperty] public partial string NewTopic { get; set; } = "";

    public void OnNavigatedTo(object? parameter)
    {
        Guid? topic = null;
        switch (parameter)
        {
            case Guid id: _selected = id; _expanded.Add(id); break;
            case StudyLink link: _selected = link.SubjectId; _expanded.Add(link.SubjectId); topic = link.TopicId; break;
        }
        _data.Changed += OnDataChanged;
        _theme.Changed += OnThemeChanged;
        Refresh();
        if (topic is { } t && Topics.FirstOrDefault(c => c.Id == t) is { } card) TopicRequested?.Invoke(this, card);
    }

    public void OnNavigatedFrom()
    {
        _data.Changed -= OnDataChanged;
        _theme.Changed -= OnThemeChanged;
    }

    [RelayCommand]
    private async Task AddSubjectAsync()
    {
        var vm = new SubjectEditorViewModel(_data, null);
        await _dialogs.ShowAsync(new Views.Dialogs.SubjectEditorDialog(vm));
        if (vm.Saved is { } created) { _selected = created.Id; Refresh(); }
    }

    [RelayCommand]
    private async Task EditSubjectAsync()
    {
        if (_data.Subject(_selected) is not { } subject) return;
        await _dialogs.ShowAsync(new Views.Dialogs.SubjectEditorDialog(new SubjectEditorViewModel(_data, subject)));
    }

    [RelayCommand]
    private async Task DeleteSubjectAsync()
    {
        if (_data.Subject(_selected) is not { } subject) return;
        if (!await _dialogs.ConfirmAsync($"Delete {subject.Name}?",
                "Its topics and subtopics are deleted too. Notes, reminders, calendar events and focus history stay, without a subject.",
                "Delete", destructive: true)) return;
        foreach (var topic in _data.TopicsOf(subject.Id))
        {
            foreach (var s in _data.SubtopicsOf(topic.Id)) _data.Delete(s);
            _data.Delete(topic);
        }
        _data.Delete(subject);
        _selected = null;
        Refresh();
    }

    [RelayCommand]
    private void AddTopic()
    {
        var name = NewTopic.Trim();
        if (name.Length == 0 || _data.Subject(_selected) is not { } subject) return;
        var topic = _data.Save(new TopicDto
        {
            Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, SubjectId = subject.Id, Name = name, SortOrder = _data.TopicsOf(subject.Id).Count,
        });
        // Added in place rather than rebuilding the page, so the box keeps focus for the next topic.
        Topics.Add(NewCard(topic, ColorMix.Brush(subject.ColorHex)));
        HasTopics = true;
        NewTopic = "";
        _expanded.Add(subject.Id);
        RefreshTree();
        RefreshHeader();
    }

    [RelayCommand]
    private void Schedule()
    {
        if (_selected is { } id) _navigation.Navigate(NavSections.Schedule, new StudyLink(id));
    }

    [RelayCommand]
    private void NewNote() => _navigation.Navigate(NavSections.Notes, new NotesTarget(_selected, null, null, CreateNew: true));

    private void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        // Topic and subtopic edits made on this page are already on screen (the cards add and remove their own rows),
        // and rebuilding would close an open edit flyout, so only the totals are refreshed for those.
        if (!e.FromSync && (e.Kinds & ~(DataKinds.Topics | DataKinds.Subtopics)) == 0)
        {
            foreach (var card in Topics) card.RefreshProgress();
            RefreshTree();
            RefreshHeader();
            return;
        }
        if (e.Affects(DataKinds.Study | DataKinds.Notes | DataKinds.Events | DataKinds.Sessions)) Refresh();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var subjects = _data.Subjects;
        HasSubjects = subjects.Count > 0;
        if (_selected is null || subjects.All(s => s.Id != _selected)) _selected = subjects.FirstOrDefault()?.Id;

        RefreshTree();

        Topics.Clear();
        if (_data.Subject(_selected) is { } subject)
        {
            var color = ColorMix.Brush(subject.ColorHex);
            foreach (var t in _data.TopicsOf(subject.Id)) Topics.Add(NewCard(t, color));
        }
        HasTopics = Topics.Count > 0;
        RefreshHeader();
    }

    private void RefreshTree()
    {
        Tree.Clear();
        foreach (var s in _data.Subjects)
        {
            var selected = s.Id == _selected;
            var topics = _data.TopicsOf(s.Id).Select(t => new TopicLeafViewModel(t, OpenTopic)).ToList();
            var node = new SubjectNodeViewModel(s, topics, _expanded.Contains(s.Id), selected,
                _theme.Brush(selected ? "NerdySubtleFillBrush" : "NerdyTransparentBrush"), Select);
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SubjectNodeViewModel.IsExpanded)) return;
                if (node.IsExpanded) _expanded.Add(node.Id); else _expanded.Remove(node.Id);
            };
            Tree.Add(node);
        }
    }

    private void RefreshHeader()
    {
        if (_data.Subject(_selected) is not { } subject)
        {
            Name = "";
            return;
        }
        var topics = _data.TopicsOf(subject.Id).Count;
        var notes = _data.Notes.Count(n => n.SubjectId == subject.Id);
        var parts = new List<string> { $"{topics} topic{(topics == 1 ? "" : "s")}", $"{notes} note{(notes == 1 ? "" : "s")}" };
        if (_agenda.SubjectProgress(subject.Id) is { } progress) parts.Add($"{progress}% complete");
        Name = subject.Name;
        Meta = string.Join(" · ", parts);
        var c = ColorMix.Parse(subject.ColorHex);
        Color = ColorMix.Brush(c);
        Tint = ColorMix.Brush(_theme.Tint(c, 14));
        NextText = $"Next session · {_agenda.NextLabel(subject.Id)}";
        HasDue = subject.DueTitle is not null || subject.DueAt is not null;
        DueText = string.Join(" · ", new[] { subject.DueTitle, subject.DueAt is { } d ? Fmt.MonthDay(DateOnly.FromDateTime(d.LocalDateTime)) : null }.Where(x => !string.IsNullOrEmpty(x)));
        var week = FocusStats.DailyMinutes(_agenda.SessionRecords().Where(s => s.SubjectId == subject.Id), _clock.Today.AddDays(-6), 7).Sum();
        FocusText = $"Focused {Fmt.HoursMinutes(week)} this week";
        NoTopicsText = $"Break {subject.Name} into topics, such as chapters or units. Add subtopics to a topic to track your progress.";
    }

    private TopicCardViewModel NewCard(TopicDto topic, Brush color) =>
        new(topic, color, _data, _agenda, _dialogs, _navigation, card =>
        {
            Topics.Remove(card);
            HasTopics = Topics.Count > 0;
            RefreshTree();
            RefreshHeader();
        });

    private void Select(Guid id)
    {
        _selected = id;
        Refresh();
    }

    private void OpenTopic(TopicDto topic)
    {
        if (topic.SubjectId != _selected) Select(topic.SubjectId);
        if (Topics.FirstOrDefault(c => c.Id == topic.Id) is { } card) TopicRequested?.Invoke(this, card);
    }
}

/// <summary>Create or edit a subject (name, colour, upcoming milestone).</summary>
public sealed partial class SubjectEditorViewModel : ObservableObject
{
    private readonly DataService _data;
    private readonly SubjectDto? _original;

    public SubjectEditorViewModel(DataService data, SubjectDto? subject)
    {
        _data = data;
        _original = subject;
        Name = subject?.Name ?? "";
        DueTitle = subject?.DueTitle ?? "";
        DueDate = subject?.DueAt is { } d ? new DateTimeOffset(d.LocalDateTime.Date) : null;
        SelectedColor = subject?.ColorHex ?? SubjectPalette.Next(data.Subjects.Select(s => s.ColorHex));
        RebuildSwatches();
    }

    public string DialogTitle => _original is null ? "New subject" : "Edit subject";
    public ObservableCollection<SwatchItem> Swatches { get; } = [];
    public SubjectDto? Saved { get; private set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial string Name { get; set; }
    [ObservableProperty] public partial string DueTitle { get; set; }
    [ObservableProperty] public partial DateTimeOffset? DueDate { get; set; }
    [ObservableProperty] public partial string SelectedColor { get; set; }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    public void Save()
    {
        Saved = _data.Save((_original ?? new SubjectDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, SortOrder = _data.Subjects.Count }) with
        {
            Name = Name.Trim(),
            ColorHex = SelectedColor,
            DueTitle = string.IsNullOrWhiteSpace(DueTitle) ? null : DueTitle.Trim(),
            DueAt = DueDate is { } d ? new DateTimeOffset(d.Date) : null
        });
    }

    private void RebuildSwatches()
    {
        var theme = App.GetService<ThemeService>();
        Swatches.Clear();
        foreach (var hex in SubjectPalette.Colors)
            Swatches.Add(new SwatchItem(hex, ColorMix.Brush(hex),
                hex.Equals(SelectedColor, StringComparison.OrdinalIgnoreCase) ? ColorMix.Brush(hex) : theme.Brush("NerdyTransparentBrush"),
                h => { SelectedColor = h; RebuildSwatches(); }));
    }
}
