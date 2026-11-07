using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Theming;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;

namespace Nerdy.App.ViewModels;

public sealed partial class NoteListItemViewModel(NoteDto note, string meta, Brush dot, Brush background, Action<Guid> select)
{
    public Guid Id { get; } = note.Id;
    public string Title { get; } = note.Title.Length == 0 ? "Untitled note" : note.Title;
    public string Snippet { get; } = Snip(note.BodyText);
    public string Meta { get; } = meta;
    public Brush Dot { get; } = dot;
    public Brush Background { get; } = background;

    [RelayCommand]
    private void Select() => select(Id);

    private static string Snip(string text)
    {
        var line = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length > 120 ? line[..120] : line;
    }
}

public sealed partial class ChecklistItemViewModel(string text, bool done, Action changed, Action<ChecklistItemViewModel> remove) : ObservableObject
{
    [ObservableProperty] public partial string Text { get; set; } = text;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ItemOpacity))] public partial bool Done { get; set; } = done;

    /// <summary>Done items fade back (a TextBox can't strike its text through).</summary>
    public double ItemOpacity => Done ? 0.5 : 1;

    partial void OnTextChanged(string value) => changed();
    partial void OnDoneChanged(bool value) => changed();

    [RelayCommand]
    private void Remove() => remove(this);
}

public sealed partial class NotesViewModel : ObservableObject, INavigationAware
{
    private readonly DataService _data;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly DialogService _dialogs;
    private readonly FocusSessionService _focus;
    private readonly IClock _clock;
    private DispatcherQueueTimer? _saveTimer;
    private NoteDto? _current;
    private string _pendingRtf = "";
    private string _pendingText = "";
    private bool _bodyDirty;
    private bool _loading;

    public NotesViewModel(DataService data, ThemeService theme, NavigationService navigation, DialogService dialogs, FocusSessionService focus, IClock clock)
    {
        _data = data;
        _theme = theme;
        _navigation = navigation;
        _dialogs = dialogs;
        _focus = focus;
        _clock = clock;
    }

    public ObservableCollection<NoteListItemViewModel> Notes { get; } = [];
    public ObservableCollection<ChecklistItemViewModel> Checklist { get; } = [];
    public ObservableCollection<Option<Guid?>> SubjectOptions { get; } = [];
    public ObservableCollection<Option<Guid?>> TopicOptions { get; } = [];
    public ObservableCollection<Option<Guid?>> SubtopicOptions { get; } = [];
    /// <summary>"All subjects", then each subject followed by its topics. A null value shows every note.</summary>
    public ObservableCollection<Option<StudyLink?>> FilterOptions { get; } = [];

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial Option<StudyLink?>? Filter { get; set; }
    [ObservableProperty] public partial bool HasNote { get; private set; }
    [ObservableProperty] public partial bool HasNotes { get; private set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial Option<Guid?>? Subject { get; set; }
    [ObservableProperty] public partial Option<Guid?>? Topic { get; set; }
    [ObservableProperty] public partial Option<Guid?>? Subtopic { get; set; }
    [ObservableProperty] public partial bool HasTopicOptions { get; private set; }
    [ObservableProperty] public partial bool HasSubtopicOptions { get; private set; }
    [ObservableProperty] public partial string EditedText { get; private set; } = "";
    [ObservableProperty] public partial Brush? SubjectDot { get; private set; }
    [ObservableProperty] public partial string NewChecklistItem { get; set; } = "";

    /// <summary>Raised when another note is opened; the page loads <see cref="CurrentBodyRtf"/> into the editor.</summary>
    public event EventHandler? NoteOpened;

    public string CurrentBodyRtf => _current?.BodyRtf ?? "";
    public string CurrentBodyText => _current?.BodyText ?? "";

    public void OnNavigatedTo(object? parameter)
    {
        _saveTimer ??= CreateSaveTimer();
        _data.Changed += OnDataChanged;
        _theme.Changed += OnThemeChanged;
        BuildFilters();
        switch (parameter)
        {
            case NotesTarget target:
                Filter = FilterOptions.FirstOrDefault(f => f.Value?.SubjectId == target.SubjectId && f.Value?.TopicId == target.TopicId)
                    ?? FilterOptions.FirstOrDefault(f => f.Value?.SubjectId == target.SubjectId && f.Value?.TopicId is null)
                    ?? FilterOptions[0];
                if (target.CreateNew) NewNote(target.SubjectId, target.TopicId, target.SubtopicId);
                else RefreshList(selectFirst: true);
                break;
            case Guid noteId when _data.Note(noteId) is { } note:
                Filter = FilterOptions[0];
                Open(note);
                break;
            default:
                Filter ??= FilterOptions[0];
                RefreshList(selectFirst: true);
                break;
        }
    }

    public void OnNavigatedFrom()
    {
        SaveNow();
        _data.Changed -= OnDataChanged;
        _theme.Changed -= OnThemeChanged;
    }

    partial void OnSearchChanged(string value) => RefreshList(selectFirst: false);
    partial void OnFilterChanged(Option<StudyLink?>? value) => RefreshList(selectFirst: false);

    partial void OnTitleChanged(string value) => MarkDirty();

    partial void OnSubjectChanged(Option<Guid?>? value)
    {
        if (_loading) return;
        BuildTopicOptions(value?.Value, null);
        BuildSubtopicOptions(null, null);
        UpdateSubjectDot();
        MarkDirty();
    }

    partial void OnTopicChanged(Option<Guid?>? value)
    {
        if (_loading) return;
        BuildSubtopicOptions(value?.Value, null);
        MarkDirty();
    }

    partial void OnSubtopicChanged(Option<Guid?>? value) => MarkDirty();

    [RelayCommand]
    private void New() => NewNote(Filter?.Value?.SubjectId, Filter?.Value?.TopicId, null);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_current is not { } note) return;
        if (!await _dialogs.ConfirmAsync("Delete this note?", $"\"{(note.Title.Length == 0 ? "Untitled note" : note.Title)}\" will be deleted on all your devices.", "Delete", destructive: true)) return;
        _saveTimer?.Stop();
        _data.Delete(note);
        _current = null;
        RefreshList(selectFirst: true);
    }

    [RelayCommand]
    private void FocusOnNote()
    {
        SaveNow();
        _focus.DraftTask = Title.Trim().Length == 0 ? "Study notes" : Title.Trim();
        _focus.DraftSubjectId = Subject?.Value;
        _focus.DraftTopicId = Topic?.Value;
        _navigation.Navigate(NavSections.Focus);
    }

    [RelayCommand]
    private void AddChecklistItem()
    {
        var text = NewChecklistItem.Trim();
        if (text.Length == 0) return;
        Checklist.Add(new ChecklistItemViewModel(text, false, MarkDirty, RemoveChecklistItem));
        NewChecklistItem = "";
        MarkDirty();
    }

    /// <summary>Called by the editor whenever the body changes.</summary>
    public void UpdateBody(string rtf, string text)
    {
        if (_loading || _current is null) return;
        _pendingRtf = rtf;
        _pendingText = text;
        _bodyDirty = true;
        MarkDirty();
    }

    private void NewNote(Guid? subjectId, Guid? topicId, Guid? subtopicId)
    {
        SaveNow();
        var note = _data.Save(new NoteDto
        {
            Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, SubjectId = subjectId,
            TopicId = subjectId is null ? null : topicId, SubtopicId = subjectId is null || topicId is null ? null : subtopicId,
        });
        Search = "";
        RefreshList(selectFirst: false);
        Open(note);
    }

    private void Open(NoteDto note)
    {
        SaveNow();
        _loading = true;
        _current = note;
        _bodyDirty = false;
        Title = note.Title;
        BuildSubjectOptions();
        Subject = SubjectOptions.FirstOrDefault(s => s.Value == note.SubjectId) ?? SubjectOptions[0];
        BuildTopicOptions(Subject.Value, note.TopicId);
        BuildSubtopicOptions(Topic?.Value, note.SubtopicId);
        Checklist.Clear();
        foreach (var c in note.Checklist) Checklist.Add(new ChecklistItemViewModel(c.Text, c.Done, MarkDirty, RemoveChecklistItem));
        HasNote = true;
        UpdateSubjectDot();
        UpdateEdited();
        _loading = false;
        NoteOpened?.Invoke(this, EventArgs.Empty);
        RefreshList(selectFirst: false);
    }

    private void RemoveChecklistItem(ChecklistItemViewModel item)
    {
        Checklist.Remove(item);
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (_loading || _current is null || _saveTimer is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer?.Stop();
        if (_current is not { } note || _loading) return;
        var updated = note with
        {
            Title = Title.Trim(),
            SubjectId = Subject?.Value,
            TopicId = Topic?.Value,
            SubtopicId = Subtopic?.Value,
            BodyRtf = _bodyDirty ? _pendingRtf : note.BodyRtf,
            BodyText = _bodyDirty ? _pendingText.TrimEnd() : note.BodyText,
            Checklist = Checklist.Select(c => new ChecklistItemDto { Text = c.Text, Done = c.Done }).ToList()
        };
        if (updated.Title == note.Title && updated.SubjectId == note.SubjectId && updated.TopicId == note.TopicId && updated.SubtopicId == note.SubtopicId &&
            updated.BodyRtf == note.BodyRtf && updated.BodyText == note.BodyText && updated.Checklist.SequenceEqual(note.Checklist))
            return;
        _current = _data.Save(updated);
        _bodyDirty = false;
        UpdateEdited();
    }

    private DispatcherQueueTimer CreateSaveTimer()
    {
        var t = DispatcherQueue.GetForCurrentThread().CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(700);
        t.IsRepeating = false;
        t.Tick += (_, _) => SaveNow();
        return t;
    }

    private void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (!e.Affects(DataKinds.Notes | DataKinds.Study)) return;
        if (e.FromSync && _current is { } current && _data.Note(current.Id) is { } fresh && fresh.UpdatedAt != current.UpdatedAt && !_bodyDirty)
            Open(fresh); // edited on another device and not being edited here
        else if (e.FromSync && _current is { } gone && _data.Note(gone.Id) is null)
        {
            _current = null;
            RefreshList(selectFirst: true);
            return;
        }
        if (e.Affects(DataKinds.Subjects | DataKinds.Topics)) BuildFilters();
        RefreshList(selectFirst: false);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateSubjectDot();
        RefreshList(selectFirst: false);
    }

    private void RefreshList(bool selectFirst)
    {
        var q = Search.Trim();
        var filter = Filter?.Value;
        var notes = _data.Notes
            .Where(n => filter is null || (n.SubjectId == filter.SubjectId && (filter.TopicId is null || n.TopicId == filter.TopicId)))
            .Where(n => q.Length == 0 || n.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || n.BodyText.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();

        Notes.Clear();
        var now = _clock.LocalNow;
        foreach (var n in notes)
        {
            var where = _data.Subtopic(n.SubtopicId)?.Name ?? _data.Topic(n.TopicId)?.Name ?? _data.Subject(n.SubjectId)?.Name ?? AgendaService.NoSubjectName;
            Notes.Add(new NoteListItemViewModel(n, $"{where} · {Fmt.Ago(n.UpdatedAt.LocalDateTime, now)}",
                ColorMix.Brush(_data.Subject(n.SubjectId)?.ColorHex ?? AgendaService.NoSubjectColor),
                _theme.Brush(n.Id == _current?.Id ? "NerdyCardBackgroundBrush" : "NerdyTransparentBrush"),
                id => { if (_data.Note(id) is { } note) Open(note); }));
        }
        HasNotes = Notes.Count > 0;

        if (selectFirst || (_current is not null && _data.Note(_current.Id) is null))
        {
            if (notes.FirstOrDefault() is { } first) Open(first);
            else { _current = null; HasNote = false; }
        }
    }

    private void BuildFilters()
    {
        var selected = Filter?.Value;
        FilterOptions.Clear();
        FilterOptions.Add(new Option<StudyLink?>("All subjects", null));
        foreach (var subject in _data.Subjects)
        {
            FilterOptions.Add(new Option<StudyLink?>(subject.Name, new StudyLink(subject.Id)));
            foreach (var t in _data.TopicsOf(subject.Id)) FilterOptions.Add(new Option<StudyLink?>($"{subject.Name} › {t.Name}", new StudyLink(subject.Id, t.Id)));
        }
        Filter = FilterOptions.FirstOrDefault(f => f.Value == selected) ?? FilterOptions[0];
    }

    private void BuildSubjectOptions()
    {
        SubjectOptions.Clear();
        SubjectOptions.Add(new Option<Guid?>(AgendaService.NoSubjectName, null));
        foreach (var s in _data.Subjects) SubjectOptions.Add(new Option<Guid?>(s.Name, s.Id));
    }

    private void BuildTopicOptions(Guid? subjectId, Guid? selected)
    {
        var wasLoading = _loading;
        _loading = true;
        TopicOptions.Clear();
        TopicOptions.Add(new Option<Guid?>("No topic", null));
        if (subjectId is { } id) foreach (var t in _data.TopicsOf(id)) TopicOptions.Add(new Option<Guid?>(t.Name, t.Id));
        Topic = TopicOptions.FirstOrDefault(t => t.Value == selected) ?? TopicOptions[0];
        HasTopicOptions = TopicOptions.Count > 1;
        _loading = wasLoading;
    }

    private void BuildSubtopicOptions(Guid? topicId, Guid? selected)
    {
        var wasLoading = _loading;
        _loading = true;
        SubtopicOptions.Clear();
        SubtopicOptions.Add(new Option<Guid?>("No subtopic", null));
        if (topicId is { } id) foreach (var s in _data.SubtopicsOf(id)) SubtopicOptions.Add(new Option<Guid?>(s.Name, s.Id));
        Subtopic = SubtopicOptions.FirstOrDefault(s => s.Value == selected) ?? SubtopicOptions[0];
        HasSubtopicOptions = SubtopicOptions.Count > 1;
        _loading = wasLoading;
    }

    private void UpdateSubjectDot() => SubjectDot = ColorMix.Brush(_data.Subject(Subject?.Value)?.ColorHex ?? AgendaService.NoSubjectColor);

    private void UpdateEdited() => EditedText = _current is null ? "" : $"Edited {Fmt.Ago(_current.UpdatedAt.LocalDateTime, _clock.LocalNow)}";
}
