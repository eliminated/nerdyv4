using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nerdy.App.Services.Data;

namespace Nerdy.App.ViewModels;

/// <summary>
/// The "Subject, then topic" pair of drop-downs used by the event, reminder and note editors. Choosing a subject fills
/// the topic list with that subject's topics; the topic is optional and is hidden when the subject has none.
/// </summary>
public sealed partial class StudyPicker : ObservableObject
{
    public const string NoSubject = "No subject";
    public const string WholeSubject = "Whole subject";

    private readonly DataService _data;
    private bool _loading;

    public StudyPicker(DataService data, Guid? subjectId, Guid? topicId)
    {
        _data = data;
        Subjects = [new Option<Guid?>(NoSubject, null), .. data.Subjects.Select(s => new Option<Guid?>(s.Name, s.Id))];
        Load(subjectId, topicId);
    }

    public IReadOnlyList<Option<Guid?>> Subjects { get; }
    public ObservableCollection<Option<Guid?>> Topics { get; } = [];

    [ObservableProperty] public partial Option<Guid?> Subject { get; set; } = new(NoSubject, null);
    [ObservableProperty] public partial Option<Guid?> Topic { get; set; } = new(WholeSubject, null);
    [ObservableProperty] public partial bool HasTopics { get; private set; }

    public Guid? SubjectId => Subject?.Value;
    /// <summary>Only ever a topic of <see cref="SubjectId"/>.</summary>
    public Guid? TopicId => SubjectId is null ? null : Topic?.Value;

    /// <summary>Raised when the user picks a different subject or topic.</summary>
    public event EventHandler? Changed;

    /// <summary>Shows another item's link without raising <see cref="Changed"/>.</summary>
    public void Load(Guid? subjectId, Guid? topicId)
    {
        _loading = true;
        Subject = Subjects.FirstOrDefault(s => s.Value == subjectId) ?? Subjects[0];
        FillTopics(topicId);
        _loading = false;
    }

    partial void OnSubjectChanged(Option<Guid?> value)
    {
        if (_loading || value is null) return;
        FillTopics(null);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnTopicChanged(Option<Guid?> value)
    {
        // A ComboBox briefly reports no selection while its list is being refilled.
        if (_loading || value is null) return;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void FillTopics(Guid? topicId)
    {
        var wasLoading = _loading;
        _loading = true;
        Topics.Clear();
        Topics.Add(new Option<Guid?>(WholeSubject, null));
        if (Subject?.Value is { } id)
            foreach (var t in _data.TopicsOf(id)) Topics.Add(new Option<Guid?>(t.Name, t.Id));
        HasTopics = Topics.Count > 1;
        Topic = Topics.FirstOrDefault(t => t.Value == topicId) ?? Topics[0];
        _loading = wasLoading;
    }
}
