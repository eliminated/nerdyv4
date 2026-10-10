using System.Text.Json;
using Nerdy.Api.Data.Entities;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Sync;

/// <summary>Copies client-owned fields between DTOs and entities. Owner and version are set by <see cref="SyncService"/>.</summary>
internal static class SyncMappings
{
    private static readonly JsonSerializerOptions ChecklistJson = new(JsonSerializerDefaults.Web);

    private static void ApplyCommon(SyncEntity entity, SyncItemDto dto)
    {
        entity.UpdatedAt = dto.UpdatedAt;
        entity.IsDeleted = dto.IsDeleted;
    }

    public static void Apply(Topic e, TopicDto d)
    {
        ApplyCommon(e, d);
        e.Name = d.Name;
        e.ColorHex = d.ColorHex;
        e.DueTitle = d.DueTitle;
        e.DueAt = d.DueAt;
        e.SortOrder = d.SortOrder;
    }

    public static TopicDto ToDto(Topic e) => new()
    {
        Id = e.Id, UpdatedAt = e.UpdatedAt, IsDeleted = e.IsDeleted,
        Name = e.Name, ColorHex = e.ColorHex, DueTitle = e.DueTitle, DueAt = e.DueAt, SortOrder = e.SortOrder,
    };

    public static void Apply(Subtopic e, SubtopicDto d)
    {
        ApplyCommon(e, d);
        e.TopicId = d.TopicId;
        e.Name = d.Name;
        e.Progress = d.Progress;
        e.SortOrder = d.SortOrder;
    }

    public static SubtopicDto ToDto(Subtopic e) => new()
    {
        Id = e.Id, UpdatedAt = e.UpdatedAt, IsDeleted = e.IsDeleted,
        TopicId = e.TopicId, Name = e.Name, Progress = e.Progress, SortOrder = e.SortOrder,
    };

    public static void Apply(CalendarEvent e, CalendarEventDto d)
    {
        ApplyCommon(e, d);
        e.Title = d.Title;
        e.Subtitle = d.Subtitle;
        e.TopicId = d.TopicId;
        e.StartsAt = d.StartsAt;
        e.EndsAt = d.EndsAt;
        e.IsAllDay = d.IsAllDay;
        e.IsMilestone = d.IsMilestone;
        e.RepeatDays = d.RepeatDays;
        e.RepeatUntil = d.RepeatUntil;
        e.ReminderMinutes = d.ReminderMinutes;
        e.AutoFocus = d.AutoFocus;
        e.AutoFocusMode = d.AutoFocusMode;
        e.SeriesId = d.SeriesId;
    }

    public static CalendarEventDto ToDto(CalendarEvent e) => new()
    {
        Id = e.Id, UpdatedAt = e.UpdatedAt, IsDeleted = e.IsDeleted,
        Title = e.Title, Subtitle = e.Subtitle, TopicId = e.TopicId, StartsAt = e.StartsAt, EndsAt = e.EndsAt,
        IsAllDay = e.IsAllDay, IsMilestone = e.IsMilestone, RepeatDays = e.RepeatDays, RepeatUntil = e.RepeatUntil,
        ReminderMinutes = e.ReminderMinutes, AutoFocus = e.AutoFocus, AutoFocusMode = e.AutoFocusMode, SeriesId = e.SeriesId,
    };

    public static void Apply(Reminder e, ReminderDto d)
    {
        ApplyCommon(e, d);
        e.Title = d.Title;
        e.DueAt = d.DueAt;
        e.HasDueTime = d.HasDueTime;
        e.TopicId = d.TopicId;
        e.IsStarred = d.IsStarred;
        e.IsDone = d.IsDone;
        e.CompletedAt = d.CompletedAt;
        e.Notes = d.Notes;
    }

    public static ReminderDto ToDto(Reminder e) => new()
    {
        Id = e.Id, UpdatedAt = e.UpdatedAt, IsDeleted = e.IsDeleted,
        Title = e.Title, DueAt = e.DueAt, HasDueTime = e.HasDueTime, TopicId = e.TopicId, IsStarred = e.IsStarred,
        IsDone = e.IsDone, CompletedAt = e.CompletedAt, Notes = e.Notes,
    };

    public static void Apply(Note e, NoteDto d)
    {
        ApplyCommon(e, d);
        e.Title = d.Title;
        e.TopicId = d.TopicId;
        e.SubtopicId = d.SubtopicId;
        e.BodyRtf = d.BodyRtf;
        e.BodyText = d.BodyText;
        e.ChecklistJson = JsonSerializer.Serialize(d.Checklist, ChecklistJson);
    }

    public static NoteDto ToDto(Note e) => new()
    {
        Id = e.Id, UpdatedAt = e.UpdatedAt, IsDeleted = e.IsDeleted,
        Title = e.Title, TopicId = e.TopicId, SubtopicId = e.SubtopicId, BodyRtf = e.BodyRtf, BodyText = e.BodyText,
        Checklist = JsonSerializer.Deserialize<List<ChecklistItemDto>>(e.ChecklistJson, ChecklistJson) ?? [],
    };

    public static void Apply(FocusSession e, FocusSessionDto d)
    {
        ApplyCommon(e, d);
        e.TopicId = d.TopicId;
        e.Task = d.Task;
        e.Mode = d.Mode;
        e.PlannedSeconds = d.PlannedSeconds;
        e.ActualSeconds = d.ActualSeconds;
        e.StartedAt = d.StartedAt;
        e.EndedAt = d.EndedAt;
        e.Outcome = d.Outcome;
        e.ExitReason = d.ExitReason;
        e.ExitNote = d.ExitNote;
    }

    public static FocusSessionDto ToDto(FocusSession e) => new()
    {
        Id = e.Id, UpdatedAt = e.UpdatedAt, IsDeleted = e.IsDeleted,
        TopicId = e.TopicId, Task = e.Task, Mode = e.Mode, PlannedSeconds = e.PlannedSeconds, ActualSeconds = e.ActualSeconds,
        StartedAt = e.StartedAt, EndedAt = e.EndedAt, Outcome = e.Outcome, ExitReason = e.ExitReason, ExitNote = e.ExitNote,
    };

    public static PreferencesDto ToDto(UserPreferences e) => new() { UpdatedAt = e.UpdatedAt, Json = e.Json };
}
