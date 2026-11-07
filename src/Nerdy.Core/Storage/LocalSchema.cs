using System.Text.Json.Nodes;

namespace Nerdy.Core.Storage;

/// <summary>One row of the app's local store: a synced item's JSON DTO, keyed by kind and id.</summary>
public sealed record StoredRow(string Kind, string Id, string Json, bool Dirty);

/// <summary>
/// Versions of the app's local store, and the upgrades between them. Each upgrade is the same change as the server
/// migration of that release, so after it the PC and the server agree without downloading everything again.
/// </summary>
public static class LocalSchema
{
    /// <summary>1: topics and subtopics (Nerdy 0.1.0). 2: subjects, topics and subtopics.</summary>
    public const int Current = 2;

    /// <summary>
    /// Version 1 → 2 (the server's SubjectsAboveTopics migration): topics become subjects, subtopics become topics
    /// (their own progress dropped), and links move with them: topicId → subjectId, a note's subtopicId → topicId.
    /// Dirty flags are kept, so edits that hadn't synced yet still do.
    /// </summary>
    public static List<StoredRow> AddSubjects(IEnumerable<StoredRow> rows) => rows.Select(row => row.Kind switch
    {
        "topic" => row with { Kind = "subject" },
        "subtopic" => row with { Kind = "topic", Json = Edit(row.Json, o => { Rename(o, "topicId", "subjectId"); o.Remove("progress"); }) },
        "event" or "reminder" or "session" => row with { Json = Edit(row.Json, o => Rename(o, "topicId", "subjectId")) },
        "note" => row with { Json = Edit(row.Json, o => { Rename(o, "topicId", "subjectId"); Rename(o, "subtopicId", "topicId"); }) },
        _ => row,
    }).ToList();

    /// <summary>The record of a focus session that was running when the old version closed.</summary>
    public static string? AddSubjectsToActiveSession(string? json) =>
        json is null ? null : Edit(json, o => Rename(o, "topicId", "subjectId"));

    private static string Edit(string json, Action<JsonObject> edit)
    {
        if (JsonNode.Parse(json) is not JsonObject obj) return json;
        edit(obj);
        return obj.ToJsonString();
    }

    private static void Rename(JsonObject obj, string from, string to)
    {
        if (!obj.TryGetPropertyValue(from, out var value)) return;
        obj.Remove(from);
        obj[to] = value;
    }
}
