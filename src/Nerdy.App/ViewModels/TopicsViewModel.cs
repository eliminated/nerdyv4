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
using Nerdy.Core.Time;
using Nerdy.Core.Topics;
using Windows.UI.Text;

namespace Nerdy.App.ViewModels;

public sealed partial class TopicNodeViewModel(TopicDto topic, int subCount, IReadOnlyList<string> subtopics, bool expanded, bool selected,
    Brush rowBackground, Action<Guid> select) : ObservableObject
{
    public Guid Id { get; } = topic.Id;
    public string Name { get; } = topic.Name;
    public Brush Color { get; } = ColorMix.Brush(topic.ColorHex);
    public string SubCount { get; } = subCount.ToString();
    public IReadOnlyList<string> Subtopics { get; } = subtopics;
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

/// <summary>A subtopic row; progress edits are saved shortly after the slider stops moving.</summary>
public sealed partial class SubtopicRowViewModel : ObservableObject
{
    private readonly DataService _data;
    private readonly DispatcherQueueTimer _saveTimer;
    private SubtopicDto _subtopic;

    public SubtopicRowViewModel(SubtopicDto subtopic, int notes, Brush color, DataService data, Action<Guid> openNotes)
    {
        _subtopic = subtopic;
        _data = data;
        Name = subtopic.Name;
        Progress = subtopic.Progress;
        NotesText = $"{notes} note{(notes == 1 ? "" : "s")}";
        Color = color;
        OpenNotesAction = () => openNotes(subtopic.Id);
        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => _subtopic = _data.Save(_subtopic with { Progress = (int)Math.Round(Progress), Name = Name.Trim().Length == 0 ? _subtopic.Name : Name.Trim() });
    }

    private Action OpenNotesAction { get; }

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
    private void OpenNotes() => OpenNotesAction();

    [RelayCommand]
    private void Delete() => _data.Delete(_subtopic);
}

public sealed partial class TopicsViewModel : ObservableObject, INavigationAware
{
    private readonly DataService _data;
    private readonly AgendaService _agenda;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly IClock _clock;
    private readonly HashSet<Guid> _expanded = [];
    private Guid? _selected;

    public TopicsViewModel(DataService data, AgendaService agenda, ThemeService theme, NavigationService navigation,
        DialogService dialogs, ToastService toasts, IClock clock)
    {
        _data = data;
        _agenda = agenda;
        _theme = theme;
        _navigation = navigation;
        _dialogs = dialogs;
        _toasts = toasts;
        _clock = clock;
    }

    public ObservableCollection<TopicNodeViewModel> Tree { get; } = [];
    public ObservableCollection<SubtopicRowViewModel> Subtopics { get; } = [];

    [ObservableProperty] public partial bool HasTopics { get; private set; }
    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string Meta { get; private set; } = "";
    [ObservableProperty] public partial Brush? Color { get; private set; }
    [ObservableProperty] public partial Brush? Tint { get; private set; }
    [ObservableProperty] public partial string NextText { get; private set; } = "";
    [ObservableProperty] public partial string DueText { get; private set; } = "";
    [ObservableProperty] public partial bool HasDue { get; private set; }
    [ObservableProperty] public partial string FocusText { get; private set; } = "";
    [ObservableProperty] public partial string NewSubtopic { get; set; } = "";

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is Guid id) { _selected = id; _expanded.Add(id); }
        _data.Changed += OnDataChanged;
        _theme.Changed += OnThemeChanged;
        Refresh();
    }

    public void OnNavigatedFrom()
    {
        _data.Changed -= OnDataChanged;
        _theme.Changed -= OnThemeChanged;
    }

    [RelayCommand]
    private async Task AddTopicAsync()
    {
        var vm = new TopicEditorViewModel(_data, null);
        await _dialogs.ShowAsync(new Views.Dialogs.TopicEditorDialog(vm));
        if (vm.Saved is { } created) { _selected = created.Id; Refresh(); }
    }

    [RelayCommand]
    private async Task EditTopicAsync()
    {
        if (_data.Topic(_selected) is not { } topic) return;
        await _dialogs.ShowAsync(new Views.Dialogs.TopicEditorDialog(new TopicEditorViewModel(_data, topic)));
    }

    [RelayCommand]
    private async Task DeleteTopicAsync()
    {
        if (_data.Topic(_selected) is not { } topic) return;
        if (!await _dialogs.ConfirmAsync($"Delete {topic.Name}?",
                "Its subtopics are deleted too. Notes, reminders and calendar events stay, without a topic.", "Delete", destructive: true)) return;
        foreach (var s in _data.SubtopicsOf(topic.Id)) _data.Delete(s);
        _data.Delete(topic);
        _selected = null;
        Refresh();
    }

    [RelayCommand]
    private void AddSubtopic()
    {
        var name = NewSubtopic.Trim();
        if (name.Length == 0 || _selected is not { } topicId) return;
        _data.Save(new SubtopicDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, TopicId = topicId, Name = name, SortOrder = _data.SubtopicsOf(topicId).Count });
        NewSubtopic = "";
        Refresh();
    }

    [RelayCommand]
    private void Schedule() => _navigation.Navigate(NavSections.Schedule, _selected);

    [RelayCommand]
    private void NewNote() => _navigation.Navigate(NavSections.Notes, new NotesTarget(_selected, null, CreateNew: true));

    private void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        // Local subtopic edits (progress slider, rename) are already reflected in their rows; rebuilding would
        // close the edit flyout, so only refresh the summary for those.
        if (!e.FromSync && e.Kinds == DataKinds.Subtopics) { RefreshHeader(); return; }
        if (e.Affects(DataKinds.Topics | DataKinds.Subtopics | DataKinds.Notes | DataKinds.Events | DataKinds.Sessions)) Refresh();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var topics = _data.Topics;
        HasTopics = topics.Count > 0;
        if (_selected is null || topics.All(t => t.Id != _selected)) _selected = topics.FirstOrDefault()?.Id;

        Tree.Clear();
        foreach (var t in topics)
        {
            var subs = _data.SubtopicsOf(t.Id);
            var selected = t.Id == _selected;
            var node = new TopicNodeViewModel(t, subs.Count, subs.Select(s => s.Name).ToList(), _expanded.Contains(t.Id), selected,
                _theme.Brush(selected ? "NerdySubtleFillBrush" : "NerdyTransparentBrush"), Select);
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(TopicNodeViewModel.IsExpanded)) return;
                if (node.IsExpanded) _expanded.Add(node.Id); else _expanded.Remove(node.Id);
            };
            Tree.Add(node);
        }

        Subtopics.Clear();
        if (_data.Topic(_selected) is { } topic)
        {
            var color = ColorMix.Brush(topic.ColorHex);
            foreach (var s in _data.SubtopicsOf(topic.Id))
                Subtopics.Add(new SubtopicRowViewModel(s, _data.Notes.Count(n => n.SubtopicId == s.Id), color, _data,
                    id => _navigation.Navigate(NavSections.Notes, new NotesTarget(topic.Id, id, CreateNew: false))));
        }
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        if (_data.Topic(_selected) is not { } topic)
        {
            Name = "";
            return;
        }
        var subs = _data.SubtopicsOf(topic.Id);
        var notes = _data.Notes.Count(n => n.TopicId == topic.Id);
        Name = topic.Name;
        Meta = $"{subs.Count} subtopic{(subs.Count == 1 ? "" : "s")} · {notes} note{(notes == 1 ? "" : "s")} · {_agenda.TopicProgress(topic.Id)}% complete";
        var c = ColorMix.Parse(topic.ColorHex);
        Color = ColorMix.Brush(c);
        Tint = ColorMix.Brush(_theme.Tint(c, 14));
        NextText = $"Next session · {_agenda.NextLabel(topic.Id)}";
        HasDue = topic.DueTitle is not null || topic.DueAt is not null;
        DueText = string.Join(" · ", new[] { topic.DueTitle, topic.DueAt is { } d ? Fmt.MonthDay(DateOnly.FromDateTime(d.LocalDateTime)) : null }.Where(x => !string.IsNullOrEmpty(x)));
        var week = FocusStats.DailyMinutes(_agenda.SessionRecords().Where(s => s.TopicId == topic.Id), _clock.Today.AddDays(-6), 7).Sum();
        FocusText = $"Focused {Fmt.HoursMinutes(week)} this week";
    }

    private void Select(Guid id)
    {
        _selected = id;
        Refresh();
    }
}

/// <summary>Where the Notes page should open: a topic/subtopic filter, optionally creating a note there.</summary>
public sealed record NotesTarget(Guid? TopicId, Guid? SubtopicId, bool CreateNew);

/// <summary>Create or edit a topic (name, colour, upcoming milestone).</summary>
public sealed partial class TopicEditorViewModel : ObservableObject
{
    private readonly DataService _data;
    private readonly TopicDto? _original;

    public TopicEditorViewModel(DataService data, TopicDto? topic)
    {
        _data = data;
        _original = topic;
        Name = topic?.Name ?? "";
        DueTitle = topic?.DueTitle ?? "";
        DueDate = topic?.DueAt is { } d ? new DateTimeOffset(d.LocalDateTime.Date) : null;
        SelectedColor = topic?.ColorHex ?? TopicPalette.Next(data.Topics.Select(t => t.ColorHex));
        RebuildSwatches();
    }

    public string DialogTitle => _original is null ? "New topic" : "Edit topic";
    public ObservableCollection<SwatchItem> Swatches { get; } = [];
    public TopicDto? Saved { get; private set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] public partial string Name { get; set; }
    [ObservableProperty] public partial string DueTitle { get; set; }
    [ObservableProperty] public partial DateTimeOffset? DueDate { get; set; }
    [ObservableProperty] public partial string SelectedColor { get; set; }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    public void Save()
    {
        Saved = _data.Save((_original ?? new TopicDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, SortOrder = _data.Topics.Count }) with
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
        foreach (var hex in TopicPalette.Colors)
            Swatches.Add(new SwatchItem(hex, ColorMix.Brush(hex),
                hex.Equals(SelectedColor, StringComparison.OrdinalIgnoreCase) ? ColorMix.Brush(hex) : theme.Brush("NerdyTransparentBrush"),
                h => { SelectedColor = h; RebuildSwatches(); }));
    }
}
