using FluentIcons.Common;
using Nerdy.App.Models;
using Nerdy.App.Services.Data;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;

namespace Nerdy.App.Services;

public sealed record SearchResult(string Title, string Subtitle, Symbol Icon, string Section, object? Parameter, int Score);

/// <summary>Ctrl+K search across topics, subtopics, notes, reminders and calendar events.</summary>
public sealed class SearchService(DataService data, IClock clock)
{
    public IReadOnlyList<SearchResult> Search(string query, int limit = 8)
    {
        var q = query.Trim();
        if (q.Length == 0 || !data.IsOpen) return [];
        var today = clock.Today;
        var results = new List<SearchResult>();

        void Add(string title, string subtitle, Symbol icon, string section, object? parameter, string? body = null)
        {
            var score = Score(title, q);
            if (score == 0 && body is not null && body.Contains(q, StringComparison.CurrentCultureIgnoreCase)) score = 1;
            if (score > 0) results.Add(new SearchResult(title, subtitle, icon, section, parameter, score));
        }

        foreach (var subject in data.Subjects)
        {
            Add(subject.Name, "Subject", Symbol.Library, NavSections.Subjects, subject.Id);
            foreach (var topic in data.TopicsOf(subject.Id))
            {
                var link = new ViewModels.StudyLink(subject.Id, topic.Id);
                Add(topic.Name, $"Topic · {subject.Name}", Symbol.Folder, NavSections.Subjects, link);
                foreach (var s in data.SubtopicsOf(topic.Id)) Add(s.Name, $"Subtopic · {subject.Name} · {topic.Name}", Symbol.TextBulletList, NavSections.Subjects, link);
            }
        }
        foreach (var n in data.Notes)
            Add(n.Title.Length > 0 ? n.Title : "Untitled note", $"Note · {data.Subject(n.SubjectId)?.Name ?? AgendaService.NoSubjectName}", Symbol.Notepad, NavSections.Notes, n.Id, n.BodyText);
        foreach (var r in data.Reminders)
            Add(r.Title, r.DueAt is { } due ? $"Reminder · {Fmt.Due(due.LocalDateTime, r.HasDueTime, today)}" : "Reminder", Symbol.Alert, NavSections.Reminders, r.Id, r.Notes);
        foreach (var e in data.Events.Where(e => e.RepeatDays != 0 || DateOnly.FromDateTime(e.StartsAt.LocalDateTime) >= today.AddDays(-7)))
            Add(e.Title, $"Event · {Fmt.ShortDay(DateOnly.FromDateTime(e.StartsAt.LocalDateTime))}", Symbol.Calendar, NavSections.Calendar, DateOnly.FromDateTime(e.StartsAt.LocalDateTime), e.Subtitle);

        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).Take(limit).ToList();
    }

    /// <summary>Prefix of the whole title beats prefix of a word beats substring.</summary>
    private static int Score(string text, string q)
    {
        if (text.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) return 4;
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(q, StringComparison.CurrentCultureIgnoreCase))) return 3;
        return text.Contains(q, StringComparison.CurrentCultureIgnoreCase) ? 2 : 0;
    }
}
