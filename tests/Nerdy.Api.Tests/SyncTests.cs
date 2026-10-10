using System.Net;
using System.Net.Http.Json;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

public sealed class SyncTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task First_sync_inserts_rows_and_returns_the_new_version()
    {
        var user = await factory.RegisterAsync();
        var topic = Topic("Biology", T0);
        var note = new NoteDto
        {
            Id = Guid.NewGuid(), UpdatedAt = T0, Title = "Cells", TopicId = topic.Id, BodyRtf = @"{\rtf1 Cells}", BodyText = "Cells",
            Checklist = [new ChecklistItemDto { Text = "Mitosis", Done = true }, new ChecklistItemDto { Text = "Meiosis" }],
        };

        var response = await user.Client.SyncAsync(0, new SyncChanges { Topics = [topic], Notes = [note] });

        Assert.Equal(1, response.Version);
        Assert.Equal(topic, Assert.Single(response.Changes.Topics));
        var savedNote = Assert.Single(response.Changes.Notes);
        Assert.Equal(note.Checklist, savedNote.Checklist);
        Assert.Equal(note.BodyRtf, savedNote.BodyRtf);
        Assert.Equal(T0, savedNote.UpdatedAt);
    }

    [Fact]
    public async Task A_second_device_pulls_what_the_first_pushed_and_then_only_newer_changes()
    {
        var user = await factory.RegisterAsync();
        var laptop = user.Client;
        var desktop = factory.CreateApiClient();
        desktop.DefaultRequestHeaders.Authorization = laptop.DefaultRequestHeaders.Authorization;

        var session = new FocusSessionDto
        {
            Id = Guid.NewGuid(), UpdatedAt = T0, Task = "Read chapter 3", Mode = FocusMode.Ultra, PlannedSeconds = 1500,
            ActualSeconds = 1500, StartedAt = T0, EndedAt = T0.AddMinutes(25), Outcome = FocusOutcome.Completed,
        };
        var pushed = await laptop.SyncAsync(0, new SyncChanges
        {
            Events = [new CalendarEventDto { Id = Guid.NewGuid(), UpdatedAt = T0, Title = "Exam", StartsAt = T0, EndsAt = T0.AddHours(2), IsMilestone = true, RepeatUntil = new DateOnly(2026, 12, 1) }],
            FocusSessions = [session],
        });

        var pulled = await desktop.SyncAsync(0);
        Assert.Single(pulled.Changes.Events);
        Assert.Equal(session, Assert.Single(pulled.Changes.FocusSessions));
        Assert.True(pulled.Version > pushed.Version);

        var nothingNew = await desktop.SyncAsync(pulled.Version);
        Assert.Empty(nothingNew.Changes.Events);
        Assert.Empty(nothingNew.Changes.FocusSessions);

        var reminder = new ReminderDto { Id = Guid.NewGuid(), UpdatedAt = T0, Title = "Buy flashcards", IsStarred = true };
        await laptop.SyncAsync(pushed.Version, new SyncChanges { Reminders = [reminder] });
        var incremental = await desktop.SyncAsync(nothingNew.Version);
        Assert.Equal(reminder, Assert.Single(incremental.Changes.Reminders));
        Assert.Empty(incremental.Changes.Events);
    }

    [Fact]
    public async Task Last_write_wins_newer_overwrites_and_older_is_rejected_with_the_server_copy()
    {
        var user = await factory.RegisterAsync();
        var original = Topic("Physics", T0);
        var v1 = (await user.Client.SyncAsync(0, new SyncChanges { Topics = [original] })).Version;

        var newer = original with { Name = "Physics II", UpdatedAt = T0.AddMinutes(5) };
        var afterNewer = await user.Client.SyncAsync(v1, new SyncChanges { Topics = [newer] });
        Assert.Equal("Physics II", Assert.Single(afterNewer.Changes.Topics).Name);

        // A stale device pushes an edit made before the newer one: the server keeps its copy and sends it back.
        var stale = original with { Name = "Phys", UpdatedAt = T0.AddMinutes(1) };
        var afterStale = await user.Client.SyncAsync(afterNewer.Version, new SyncChanges { Topics = [stale] });
        Assert.Equal(newer, Assert.Single(afterStale.Changes.Topics));

        // An equal timestamp is applied (ties go to the client), so re-pushing is idempotent.
        var tie = newer with { SortOrder = 3 };
        var afterTie = await user.Client.SyncAsync(afterStale.Version, new SyncChanges { Topics = [tie] });
        Assert.Equal(3, Assert.Single(afterTie.Changes.Topics).SortOrder);
    }

    [Fact]
    public async Task Soft_deletes_propagate_to_other_devices()
    {
        var user = await factory.RegisterAsync();
        var subtopic = new SubtopicDto { Id = Guid.NewGuid(), UpdatedAt = T0, TopicId = Guid.NewGuid(), Name = "Optics", Progress = 40 };
        var v1 = (await user.Client.SyncAsync(0, new SyncChanges { Subtopics = [subtopic] })).Version;

        var otherDevice = factory.CreateApiClient();
        otherDevice.DefaultRequestHeaders.Authorization = user.Client.DefaultRequestHeaders.Authorization;
        var otherVersion = (await otherDevice.SyncAsync(0)).Version;

        await user.Client.SyncAsync(v1, new SyncChanges { Subtopics = [subtopic with { IsDeleted = true, UpdatedAt = T0.AddHours(1) }] });

        var pulled = Assert.Single((await otherDevice.SyncAsync(otherVersion)).Changes.Subtopics);
        Assert.Equal(subtopic.Id, pulled.Id);
        Assert.True(pulled.IsDeleted);
    }

    [Fact]
    public async Task A_row_id_owned_by_another_user_is_rejected_and_not_leaked()
    {
        var alice = await factory.RegisterAsync();
        var bob = await factory.RegisterAsync();
        var aliceTopic = Topic("Alice's secret diary", T0);
        await alice.Client.SyncAsync(0, new SyncChanges { Topics = [aliceTopic] });

        var hijack = aliceTopic with { Name = "Bob was here", UpdatedAt = T0.AddDays(1) };
        var bobResponse = await bob.Client.SyncAsync(0, new SyncChanges { Topics = [hijack] });
        Assert.Empty(bobResponse.Changes.Topics);

        var aliceView = await alice.Client.SyncAsync(0);
        Assert.Equal("Alice's secret diary", Assert.Single(aliceView.Changes.Topics).Name);
    }

    [Fact]
    public async Task Invalid_rows_are_a_validation_problem_and_nothing_is_applied()
    {
        var user = await factory.RegisterAsync();
        var response = await user.Client.PostAsJsonAsync("/api/sync", new SyncRequest
        {
            Changes = new SyncChanges
            {
                Topics = [Topic(new string('a', 201), T0) with { ColorHex = "red" }],
                Subtopics = [new SubtopicDto { Id = Guid.NewGuid(), UpdatedAt = T0, Progress = 101 }],
                Events = [new CalendarEventDto { Id = Guid.NewGuid(), UpdatedAt = T0, StartsAt = T0, EndsAt = T0.AddHours(-1) }],
                Notes = [new NoteDto { Id = Guid.NewGuid(), UpdatedAt = T0, Checklist = Enumerable.Repeat(new ChecklistItemDto(), 201).ToList() }],
                Preferences = new PreferencesDto { UpdatedAt = T0, Json = "{not json" },
            },
        });

        var errors = await response.ReadValidationErrorsAsync();
        Assert.Contains("changes.topics[0].name", errors);
        Assert.Contains("changes.topics[0].colorHex", errors);
        Assert.Contains("changes.subtopics[0].progress", errors);
        Assert.Contains("changes.events[0].endsAt", errors);
        Assert.Contains("changes.notes[0].checklist", errors);
        Assert.Contains("changes.preferences.json", errors);

        Assert.Empty((await user.Client.SyncAsync(0)).Changes.Topics);
    }

    [Fact]
    public async Task Too_many_rows_of_one_type_is_a_validation_problem()
    {
        var user = await factory.RegisterAsync();
        var reminders = Enumerable.Range(0, 2001).Select(_ => new ReminderDto { Id = Guid.NewGuid(), UpdatedAt = T0 }).ToList();
        var response = await user.Client.PostAsJsonAsync("/api/sync", new SyncRequest { Changes = new SyncChanges { Reminders = reminders } });

        Assert.Contains("changes.reminders", await response.ReadValidationErrorsAsync());
    }

    [Fact]
    public async Task Preferences_use_last_write_wins_too()
    {
        var user = await factory.RegisterAsync();
        var first = new PreferencesDto { UpdatedAt = T0, Json = """{"theme":"dark"}""" };
        var v1 = await user.Client.SyncAsync(0, new SyncChanges { Preferences = first });
        Assert.Equal(first, v1.Changes.Preferences);

        var stale = new PreferencesDto { UpdatedAt = T0.AddMinutes(-1), Json = """{"theme":"light"}""" };
        var rejected = await user.Client.SyncAsync(v1.Version, new SyncChanges { Preferences = stale });
        Assert.Equal(first, rejected.Changes.Preferences);

        var newer = new PreferencesDto { UpdatedAt = T0.AddMinutes(1), Json = """{"theme":"light","pomodoro":50}""" };
        var applied = await user.Client.SyncAsync(rejected.Version, new SyncChanges { Preferences = newer });
        Assert.Equal(newer, applied.Changes.Preferences);

        Assert.Null((await user.Client.SyncAsync(applied.Version)).Changes.Preferences);
    }

    [Fact]
    public async Task Sync_requires_authentication()
    {
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/sync", new SyncRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static TopicDto Topic(string name, DateTimeOffset updatedAt) =>
        new() { Id = Guid.NewGuid(), UpdatedAt = updatedAt, Name = name, ColorHex = "#22AA88", DueTitle = "Final", DueAt = T0.AddDays(30), SortOrder = 1 };
}
