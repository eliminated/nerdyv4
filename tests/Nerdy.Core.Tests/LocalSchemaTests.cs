using System.Text.Json;
using Nerdy.Contracts;
using Nerdy.Core.Storage;

namespace Nerdy.Core.Tests;

public class LocalSchemaTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Rows exactly as Nerdy 0.1.0 stored them.
    private const string Biology = """{"name":"Biology","colorHex":"#1F9D74","dueTitle":"Final","dueAt":"2026-12-01T00:00:00+08:00","sortOrder":0,"id":"1b092111-0000-0000-0000-000000000001","updatedAt":"2026-11-01T09:00:00+00:00","isDeleted":false}""";
    private const string CellStructure = """{"topicId":"1b092111-0000-0000-0000-000000000001","name":"Cell structure","progress":80,"sortOrder":2,"id":"67865236-0000-0000-0000-000000000002","updatedAt":"2026-11-01T09:00:00+00:00","isDeleted":false}""";
    private const string Note = """{"title":"Organelles","topicId":"1b092111-0000-0000-0000-000000000001","subtopicId":"67865236-0000-0000-0000-000000000002","bodyRtf":"","bodyText":"","checklist":[],"id":"aaaaaaaa-0000-0000-0000-000000000003","updatedAt":"2026-11-01T09:00:00+00:00","isDeleted":false}""";
    private const string Session = """{"topicId":"1b092111-0000-0000-0000-000000000001","task":"Read","mode":"Focus","plannedSeconds":1500,"actualSeconds":1500,"startedAt":"2026-11-01T09:00:00+00:00","endedAt":"2026-11-01T09:25:00+00:00","outcome":"Completed","exitReason":null,"exitNote":null,"id":"bbbbbbbb-0000-0000-0000-000000000004","updatedAt":"2026-11-01T09:25:00+00:00","isDeleted":false}""";

    private static readonly Guid BiologyId = Guid.Parse("1b092111-0000-0000-0000-000000000001");
    private static readonly Guid CellStructureId = Guid.Parse("67865236-0000-0000-0000-000000000002");

    [Fact]
    public void Topics_become_subjects_unchanged()
    {
        var row = Assert.Single(LocalSchema.AddSubjects([new StoredRow("topic", BiologyId.ToString(), Biology, Dirty: true)]));

        Assert.Equal(("subject", true), (row.Kind, row.Dirty));
        var subject = JsonSerializer.Deserialize<SubjectDto>(row.Json, Json)!;
        Assert.Equal(("Biology", "#1F9D74", "Final", BiologyId), (subject.Name, subject.ColorHex, subject.DueTitle, subject.Id));
    }

    [Fact]
    public void Subtopics_become_topics_of_the_subject_their_topic_became()
    {
        var row = Assert.Single(LocalSchema.AddSubjects([new StoredRow("subtopic", CellStructureId.ToString(), CellStructure, Dirty: false)]));

        Assert.Equal("topic", row.Kind);
        Assert.DoesNotContain("progress", row.Json);
        var topic = JsonSerializer.Deserialize<TopicDto>(row.Json, Json)!;
        Assert.Equal((BiologyId, "Cell structure", 2, CellStructureId), (topic.SubjectId, topic.Name, topic.SortOrder, topic.Id));
    }

    [Fact]
    public void A_note_keeps_its_place_one_level_up()
    {
        var row = Assert.Single(LocalSchema.AddSubjects([new StoredRow("note", "n", Note, Dirty: true)]));

        var note = JsonSerializer.Deserialize<NoteDto>(row.Json, Json)!;
        Assert.Equal((BiologyId, CellStructureId, (Guid?)null), (note.SubjectId, note.TopicId, note.SubtopicId));
        Assert.Equal("Organelles", note.Title);
    }

    [Fact]
    public void Sessions_events_and_reminders_link_to_the_subject()
    {
        var row = Assert.Single(LocalSchema.AddSubjects([new StoredRow("session", "s", Session, Dirty: false)]));

        var session = JsonSerializer.Deserialize<FocusSessionDto>(row.Json, Json)!;
        Assert.Equal((BiologyId, (Guid?)null, 1500), (session.SubjectId, session.TopicId, session.ActualSeconds));
    }

    [Fact]
    public void Other_rows_and_rows_without_links_pass_through()
    {
        StoredRow[] rows =
        [
            new("prefs", "prefs", """{"updatedAt":"2026-11-01T09:00:00+00:00","json":"{}"}""", false),
            new("reminder", "r", """{"title":"Call home","topicId":null,"id":"r","isDeleted":false}""", false),
        ];

        var upgraded = LocalSchema.AddSubjects(rows);

        Assert.Equal(rows[0], upgraded[0]);
        Assert.Contains("\"subjectId\":null", upgraded[1].Json);
    }

    [Fact]
    public void A_session_left_running_keeps_its_subject()
    {
        var json = LocalSchema.AddSubjectsToActiveSession("""{"id":"x","topicId":"1b092111-0000-0000-0000-000000000001","mode":"Ultra"}""")!;

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(BiologyId, doc.RootElement.GetProperty("subjectId").GetGuid());
        Assert.Equal("Ultra", doc.RootElement.GetProperty("mode").GetString());
        Assert.False(doc.RootElement.TryGetProperty("topicId", out _));
    }
}
