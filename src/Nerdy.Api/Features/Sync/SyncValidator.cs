using System.Text.Json;
using Nerdy.Api.Data;
using Nerdy.Api.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Sync;

/// <summary>Validates a whole sync request up front so that a bad row never half-applies a push.</summary>
internal static class SyncValidator
{
    public static ValidationErrors Validate(SyncRequest request)
    {
        var errors = new ValidationErrors();
        if (request.SinceVersion < 0)
        {
            errors.Add("sinceVersion", "Must be zero or greater.");
        }

        var changes = request.Changes;
        Each(errors, "changes.topics", changes.Topics, (path, t) =>
        {
            errors.MaxLength($"{path}.name", t.Name, FieldLimits.Name);
            errors.ColorHex($"{path}.colorHex", t.ColorHex);
            errors.MaxLength($"{path}.dueTitle", t.DueTitle, FieldLimits.Name);
        });

        Each(errors, "changes.subtopics", changes.Subtopics, (path, s) =>
        {
            errors.MaxLength($"{path}.name", s.Name, FieldLimits.Name);
            errors.Range($"{path}.progress", s.Progress, 0, 100);
        });

        Each(errors, "changes.events", changes.Events, (path, e) =>
        {
            errors.MaxLength($"{path}.title", e.Title, FieldLimits.Name);
            errors.MaxLength($"{path}.subtitle", e.Subtitle, FieldLimits.Name);
            if (e.EndsAt < e.StartsAt)
            {
                errors.Add($"{path}.endsAt", "Must not be before startsAt.");
            }

            errors.Range($"{path}.repeatDays", e.RepeatDays, 0, 127);
            if (e.ReminderMinutes is { } minutes)
            {
                errors.Range($"{path}.reminderMinutes", minutes, 0, int.MaxValue);
            }

            errors.Defined($"{path}.autoFocusMode", e.AutoFocusMode);
        });

        Each(errors, "changes.reminders", changes.Reminders, (path, r) =>
        {
            errors.MaxLength($"{path}.title", r.Title, FieldLimits.Name);
            errors.MaxLength($"{path}.notes", r.Notes, FieldLimits.ReminderNotes);
        });

        Each(errors, "changes.notes", changes.Notes, (path, n) =>
        {
            errors.MaxLength($"{path}.title", n.Title, FieldLimits.Name);
            errors.MaxLength($"{path}.bodyRtf", n.BodyRtf, FieldLimits.NoteBodyRtf);
            errors.MaxLength($"{path}.bodyText", n.BodyText, FieldLimits.NoteBodyText);
            if (n.Checklist.Count > FieldLimits.ChecklistItems)
            {
                errors.Add($"{path}.checklist", $"Must have at most {FieldLimits.ChecklistItems} items.");
            }
            else
            {
                for (var i = 0; i < n.Checklist.Count; i++)
                {
                    errors.MaxLength($"{path}.checklist[{i}].text", n.Checklist[i].Text, FieldLimits.ChecklistItemText);
                }
            }
        });

        Each(errors, "changes.focusSessions", changes.FocusSessions, (path, f) =>
        {
            errors.MaxLength($"{path}.task", f.Task, FieldLimits.Name);
            errors.Range($"{path}.plannedSeconds", f.PlannedSeconds, 0, int.MaxValue);
            errors.Range($"{path}.actualSeconds", f.ActualSeconds, 0, int.MaxValue);
            if (f.EndedAt < f.StartedAt)
            {
                errors.Add($"{path}.endedAt", "Must not be before startedAt.");
            }

            errors.Defined($"{path}.mode", f.Mode);
            errors.Defined($"{path}.outcome", f.Outcome);
            if (f.ExitReason is { } reason)
            {
                errors.Defined($"{path}.exitReason", reason);
            }

            errors.MaxLength($"{path}.exitNote", f.ExitNote, FieldLimits.FocusExitNote);
        });

        if (changes.Preferences is { } preferences)
        {
            ValidatePreferences(errors, preferences);
        }

        return errors;
    }

    /// <summary>Checks the per-type row limit, ids and duplicates, then the item-specific rules.</summary>
    private static void Each<T>(ValidationErrors errors, string path, List<T> items, Action<string, T> validate)
        where T : SyncItemDto
    {
        if (items.Count > FieldLimits.SyncItemsPerType)
        {
            errors.Add(path, $"At most {FieldLimits.SyncItemsPerType} items per request; send the rest in another sync.");
            return;
        }

        var seen = new HashSet<Guid>();
        for (var i = 0; i < items.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            if (items[i].Id == Guid.Empty)
            {
                errors.Add($"{itemPath}.id", "Is required.");
            }
            else if (!seen.Add(items[i].Id))
            {
                errors.Add($"{itemPath}.id", "Appears more than once in this request.");
            }

            validate(itemPath, items[i]);
        }
    }

    private static void ValidatePreferences(ValidationErrors errors, PreferencesDto preferences)
    {
        const string path = "changes.preferences.json";
        if (preferences.Json is null || preferences.Json.Length > FieldLimits.PreferencesJson)
        {
            errors.Add(path, $"Is required and must be at most {FieldLimits.PreferencesJson} characters.");
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(preferences.Json);
        }
        catch (JsonException)
        {
            errors.Add(path, "Must be a valid JSON document.");
        }
    }
}
