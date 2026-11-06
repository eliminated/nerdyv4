namespace Nerdy.Contracts;

/// <summary>
/// Base for every personal, synced entity. The client generates <see cref="Id"/> and stamps
/// <see cref="UpdatedAt"/> on every edit; deletes are soft (<see cref="IsDeleted"/>) so they sync.
/// </summary>
public abstract record SyncItemDto
{
    public required Guid Id { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public bool IsDeleted { get; init; }
}

/// <summary>
/// What is kept of a deleted item: its id, when it was deleted and that it was, so the deletion still reaches every
/// device. Everything the user wrote or scheduled in it is erased, on the server and on each PC (the privacy policy in
/// site/privacy promises this).
/// </summary>
public static class SyncTombstone
{
    public static T Of<T>(T item) where T : SyncItemDto
    {
        SyncItemDto tombstone = item switch
        {
            TopicDto => new TopicDto { Id = item.Id, UpdatedAt = item.UpdatedAt, IsDeleted = true },
            SubtopicDto => new SubtopicDto { Id = item.Id, UpdatedAt = item.UpdatedAt, IsDeleted = true },
            CalendarEventDto => new CalendarEventDto { Id = item.Id, UpdatedAt = item.UpdatedAt, IsDeleted = true },
            ReminderDto => new ReminderDto { Id = item.Id, UpdatedAt = item.UpdatedAt, IsDeleted = true },
            NoteDto => new NoteDto { Id = item.Id, UpdatedAt = item.UpdatedAt, IsDeleted = true },
            FocusSessionDto => new FocusSessionDto { Id = item.Id, UpdatedAt = item.UpdatedAt, IsDeleted = true },
            _ => throw new ArgumentException($"No tombstone defined for {item.GetType().Name}.", nameof(item)),
        };
        return (T)tombstone;
    }
}

public sealed record TopicDto : SyncItemDto
{
    public string Name { get; init; } = "";
    public string ColorHex { get; init; } = "#5B5BD6";
    public string? DueTitle { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public int SortOrder { get; init; }
}

public sealed record SubtopicDto : SyncItemDto
{
    public Guid TopicId { get; init; }
    public string Name { get; init; } = "";
    /// <summary>0–100.</summary>
    public int Progress { get; init; }
    public int SortOrder { get; init; }
}

public sealed record CalendarEventDto : SyncItemDto
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public Guid? TopicId { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public bool IsAllDay { get; init; }
    /// <summary>Exams, quizzes, deadlines: highlighted in the calendar.</summary>
    public bool IsMilestone { get; init; }
    /// <summary>Weekly repeat bitmask, Monday = 1 … Sunday = 64. 0 = does not repeat.</summary>
    public int RepeatDays { get; init; }
    public DateOnly? RepeatUntil { get; init; }
    /// <summary>Minutes before start to remind; null = no reminder.</summary>
    public int? ReminderMinutes { get; init; }
    public bool AutoFocus { get; init; }
    public FocusMode AutoFocusMode { get; init; } = FocusMode.Focus;
    /// <summary>Groups the sessions created together by one study block.</summary>
    public Guid? SeriesId { get; init; }
}

public sealed record ReminderDto : SyncItemDto
{
    public string Title { get; init; } = "";
    public DateTimeOffset? DueAt { get; init; }
    public bool HasDueTime { get; init; }
    public Guid? TopicId { get; init; }
    public bool IsStarred { get; init; }
    public bool IsDone { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Notes { get; init; }
}

public sealed record ChecklistItemDto
{
    public string Text { get; init; } = "";
    public bool Done { get; init; }
}

public sealed record NoteDto : SyncItemDto
{
    public string Title { get; init; } = "";
    public Guid? TopicId { get; init; }
    public Guid? SubtopicId { get; init; }
    /// <summary>Rich text body as RTF (what the WinUI RichEditBox reads and writes).</summary>
    public string BodyRtf { get; init; } = "";
    /// <summary>Plain-text copy of the body for search and list snippets.</summary>
    public string BodyText { get; init; } = "";
    public IReadOnlyList<ChecklistItemDto> Checklist { get; init; } = [];
}

public sealed record FocusSessionDto : SyncItemDto
{
    public Guid? TopicId { get; init; }
    public string Task { get; init; } = "";
    public FocusMode Mode { get; init; }
    public int PlannedSeconds { get; init; }
    public int ActualSeconds { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public FocusOutcome Outcome { get; init; }
    public ExitReason? ExitReason { get; init; }
    public string? ExitNote { get; init; }
}

/// <summary>
/// The user's preferences as one opaque JSON document. The app owns the schema so new settings
/// never need a server change; the server only applies last-write-wins on <see cref="UpdatedAt"/>.
/// </summary>
public sealed record PreferencesDto
{
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string Json { get; init; }
}

public sealed record SyncChanges
{
    public List<TopicDto> Topics { get; init; } = [];
    public List<SubtopicDto> Subtopics { get; init; } = [];
    public List<CalendarEventDto> Events { get; init; } = [];
    public List<ReminderDto> Reminders { get; init; } = [];
    public List<NoteDto> Notes { get; init; } = [];
    public List<FocusSessionDto> FocusSessions { get; init; } = [];
    public PreferencesDto? Preferences { get; init; }

    public bool IsEmpty =>
        Topics.Count == 0 && Subtopics.Count == 0 && Events.Count == 0 && Reminders.Count == 0 &&
        Notes.Count == 0 && FocusSessions.Count == 0 && Preferences is null;
}

public sealed record SyncRequest
{
    /// <summary>The <see cref="SyncResponse.Version"/> from the last successful sync; 0 on first sync.</summary>
    public long SinceVersion { get; init; }
    public SyncChanges Changes { get; init; } = new();
}

public sealed record SyncResponse
{
    public required long Version { get; init; }
    public required SyncChanges Changes { get; init; }
    public required DateTimeOffset ServerTime { get; init; }
}
