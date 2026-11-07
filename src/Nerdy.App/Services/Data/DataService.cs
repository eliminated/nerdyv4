using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Nerdy.App.Models;
using Nerdy.Contracts;
using Nerdy.Core.Storage;
using Nerdy.Core.Time;

namespace Nerdy.App.Services.Data;

[Flags]
public enum DataKinds
{
    None = 0,
    Subjects = 1, Topics = 2, Subtopics = 4, Events = 8, Reminders = 16, Notes = 32, Sessions = 64, Preferences = 128,
    /// <summary>Anything on the Subjects page: subjects, topics or subtopics.</summary>
    Study = Subjects | Topics | Subtopics,
    All = Study | Events | Reminders | Notes | Sessions | Preferences
}

public sealed class DataChangedEventArgs(DataKinds kinds, bool fromSync) : EventArgs
{
    public DataKinds Kinds { get; } = kinds;
    /// <summary>True when the change arrived from the server rather than from a local edit.</summary>
    public bool FromSync { get; } = fromSync;
    public bool Affects(DataKinds kinds) => (Kinds & kinds) != 0;
}

/// <summary>
/// The signed-in user's data, held in memory for instant queries and persisted to <see cref="LocalDatabase"/>.
/// All members must be used from the UI thread; persistence happens on an ordered background queue.
/// Every edit stamps <c>UpdatedAt</c> and marks the row dirty so <see cref="Sync.SyncService"/> pushes it.
/// </summary>
public sealed class DataService(IClock clock, ILogger<DataService> logger)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string PreferencesKind = "prefs";
    private const string PreferencesId = "prefs";
    /// <summary>The <see cref="LocalSchema"/> version the store is in; absent means 1 (Nerdy 0.1.0).</summary>
    private const string SchemaKey = "schema";
    /// <summary>Owned by <see cref="Focus.FocusSessionService"/>, but holds a subject id that upgrades must convert.</summary>
    private const string ActiveFocusKey = "focus.active";

    private readonly EntitySet<SubjectDto> _subjects = new("subject", DataKinds.Subjects);
    private readonly EntitySet<TopicDto> _topics = new("topic", DataKinds.Topics);
    private readonly EntitySet<SubtopicDto> _subtopics = new("subtopic", DataKinds.Subtopics);
    private readonly EntitySet<CalendarEventDto> _events = new("event", DataKinds.Events);
    private readonly EntitySet<ReminderDto> _reminders = new("reminder", DataKinds.Reminders);
    private readonly EntitySet<NoteDto> _notes = new("note", DataKinds.Notes);
    private readonly EntitySet<FocusSessionDto> _sessions = new("session", DataKinds.Sessions);

    private LocalDatabase? _db;
    private Channel<Func<Task>>? _writes;
    private Task? _writer;
    private PreferencesDto? _preferencesDto;
    private bool _preferencesDirty;
    private Preferences _preferences = new();

    public Guid? UserId { get; private set; }
    public bool IsOpen => _db is not null;

    /// <summary>Raised on the UI thread after any change (local edit or sync).</summary>
    public event EventHandler<DataChangedEventArgs>? Changed;

    /// <summary>Raised after a local edit, so sync can be scheduled.</summary>
    public event EventHandler? LocalEdit;

    public async Task OpenAsync(Guid userId)
    {
        await CloseAsync();
        var db = await LocalDatabase.OpenAsync(AppPaths.UserDatabase(userId));
        foreach (var set in Sets) set.Clear();
        _preferencesDto = null;
        _preferencesDirty = false;
        _preferences = new Preferences();

        var stored = await db.LoadAllAsync();
        if (await db.GetMetaAsync(SchemaKey) != LocalSchema.Current.ToString())
            stored = await UpgradeAsync(db, stored);

        // Deleted items saved before tombstones existed still hold their content: erase it now.
        var erased = new List<LocalDatabase.Row>();
        foreach (var row in stored)
        {
            try
            {
                if (row.Kind == PreferencesKind)
                {
                    _preferencesDto = JsonSerializer.Deserialize<PreferencesDto>(row.Json, Json);
                    _preferencesDirty = row.Dirty;
                    _preferences = Preferences.Deserialize(_preferencesDto?.Json);
                }
                else if (Sets.FirstOrDefault(s => s.Kind == row.Kind)?.LoadJson(row.Json, row.Dirty) is { } tombstone)
                {
                    erased.Add(row with { Json = tombstone });
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping unreadable local {Kind} row {Id}", row.Kind, row.Id);
            }
        }

        _db = db;
        UserId = userId;
        _writes = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
        _writer = Task.Run(() => DrainWritesAsync(_writes.Reader));
        if (erased.Count > 0)
        {
            Enqueue(d => d.UpsertAsync(erased));
            Enqueue(d => d.TruncateLogAsync());
        }
        Changed?.Invoke(this, new DataChangedEventArgs(DataKinds.All, fromSync: false));
    }

    /// <summary>Converts a store written by an older Nerdy (see <see cref="LocalSchema"/>), all in one transaction.</summary>
    private async Task<List<LocalDatabase.Row>> UpgradeAsync(LocalDatabase db, List<LocalDatabase.Row> rows)
    {
        var upgraded = LocalSchema.AddSubjects(rows.Select(r => new StoredRow(r.Kind, r.Id, r.Json, r.Dirty)))
            .Select(r => new LocalDatabase.Row(r.Kind, r.Id, r.Json, r.Dirty))
            .ToList();
        var activeFocus = LocalSchema.AddSubjectsToActiveSession(await db.GetMetaAsync(ActiveFocusKey));
        await db.ReplaceAllAsync(upgraded, new Dictionary<string, string?>
        {
            [ActiveFocusKey] = activeFocus,
            [SchemaKey] = LocalSchema.Current.ToString(),
        });
        if (rows.Count > 0) logger.LogInformation("Upgraded {Count} stored rows to local schema {Schema}", rows.Count, LocalSchema.Current);
        return upgraded;
    }

    public async Task CloseAsync()
    {
        if (_db is null) return;
        _writes?.Writer.TryComplete();
        if (_writer is not null) await _writer;
        await _db.DisposeAsync();
        _db = null;
        _writes = null;
        _writer = null;
        UserId = null;
    }

    // ---------- Queries ----------

    public IReadOnlyList<SubjectDto> Subjects => _subjects.Live.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();
    public SubjectDto? Subject(Guid? id) => id is { } g ? _subjects.Get(g) : null;
    public IReadOnlyList<TopicDto> TopicsOf(Guid subjectId) =>
        _topics.Live.Where(t => t.SubjectId == subjectId).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToList();
    public TopicDto? Topic(Guid? id) => id is { } g ? _topics.Get(g) : null;
    public IReadOnlyList<SubtopicDto> SubtopicsOf(Guid topicId) =>
        _subtopics.Live.Where(s => s.TopicId == topicId).OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();
    public SubtopicDto? Subtopic(Guid? id) => id is { } g ? _subtopics.Get(g) : null;
    /// <summary>Every subtopic in a subject, across its topics.</summary>
    public IEnumerable<SubtopicDto> SubtopicsInSubject(Guid subjectId)
    {
        var topics = _topics.Live.Where(t => t.SubjectId == subjectId).Select(t => t.Id).ToHashSet();
        return _subtopics.Live.Where(s => topics.Contains(s.TopicId));
    }
    public IEnumerable<CalendarEventDto> Events => _events.Live;
    public CalendarEventDto? Event(Guid id) => _events.Get(id);
    public IEnumerable<ReminderDto> Reminders => _reminders.Live;
    public ReminderDto? Reminder(Guid id) => _reminders.Get(id);
    public IEnumerable<NoteDto> Notes => _notes.Live;
    public NoteDto? Note(Guid id) => _notes.Get(id);
    public IEnumerable<FocusSessionDto> Sessions => _sessions.Live;

    /// <summary>A copy of the current preferences; change it and pass it to <see cref="SavePreferences"/>.</summary>
    public Preferences Preferences => _preferences.Clone();

    // ---------- Edits ----------

    public T Save<T>(T item) where T : SyncItemDto
    {
        var stamped = (T)((SyncItemDto)item with { UpdatedAt = clock.UtcNow });
        var set = SetFor<T>();
        set.Put(stamped, dirty: true);
        Persist(set.Kind, stamped.Id.ToString(), JsonSerializer.Serialize(stamped, Json), dirty: true);
        Changed?.Invoke(this, new DataChangedEventArgs(set.Changes, fromSync: false));
        LocalEdit?.Invoke(this, EventArgs.Empty);
        return stamped;
    }

    /// <summary>Keeps only a tombstone, so the deletion syncs but the content is gone from this PC and the server.</summary>
    public void Delete<T>(T item) where T : SyncItemDto
    {
        Save(SyncTombstone.Of(item));
        Enqueue(db => db.TruncateLogAsync());
    }

    /// <summary>Saves several edits as one change notification (e.g. a whole study block).</summary>
    public void SaveMany<T>(IEnumerable<T> items) where T : SyncItemDto
    {
        var set = SetFor<T>();
        var now = clock.UtcNow;
        var rows = new List<LocalDatabase.Row>();
        foreach (var item in items)
        {
            var stamped = (T)((SyncItemDto)item with { UpdatedAt = now });
            set.Put(stamped, dirty: true);
            rows.Add(new LocalDatabase.Row(set.Kind, stamped.Id.ToString(), JsonSerializer.Serialize(stamped, Json), true));
        }
        if (rows.Count == 0) return;
        Enqueue(db => db.UpsertAsync(rows));
        Changed?.Invoke(this, new DataChangedEventArgs(set.Changes, fromSync: false));
        LocalEdit?.Invoke(this, EventArgs.Empty);
    }

    public void SavePreferences(Preferences preferences)
    {
        _preferences = preferences.Clone();
        _preferencesDto = new PreferencesDto { UpdatedAt = clock.UtcNow, Json = _preferences.Serialize() };
        _preferencesDirty = true;
        Persist(PreferencesKind, PreferencesId, JsonSerializer.Serialize(_preferencesDto, Json), dirty: true);
        Changed?.Invoke(this, new DataChangedEventArgs(DataKinds.Preferences, fromSync: false));
        LocalEdit?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Meta (sync version, caches, active focus session) ----------

    public Task<string?> GetMetaAsync(string key) => _db?.GetMetaAsync(key) ?? Task.FromResult<string?>(null);

    public Task SetMetaAsync(string key, string? value)
    {
        var tcs = new TaskCompletionSource();
        Enqueue(async db =>
        {
            try { await db.SetMetaAsync(key, value); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return _db is null ? Task.CompletedTask : tcs.Task;
    }

    // ---------- Sync support ----------

    internal SyncChanges CollectDirty() => new()
    {
        Subjects = _subjects.Dirty(),
        Topics = _topics.Dirty(),
        Subtopics = _subtopics.Dirty(),
        Events = _events.Dirty(),
        Reminders = _reminders.Dirty(),
        Notes = _notes.Dirty(),
        FocusSessions = _sessions.Dirty(),
        Preferences = _preferencesDirty ? _preferencesDto : null
    };

    /// <summary>
    /// Applies a sync round-trip: rows we pushed stop being dirty unless they were edited again meanwhile,
    /// and incoming rows replace local ones unless the local copy has a newer unsynced edit.
    /// </summary>
    internal void ApplySync(SyncChanges pushed, SyncChanges incoming)
    {
        var rows = new List<LocalDatabase.Row>();
        var kinds = DataKinds.None;
        var erasing = false;

        void Apply<T>(EntitySet<T> set, List<T> sent, List<T> received) where T : SyncItemDto
        {
            foreach (var p in sent)
                if (set.ClearDirtyIfUnchanged(p))
                    rows.Add(new LocalDatabase.Row(set.Kind, p.Id.ToString(), JsonSerializer.Serialize(set.Raw(p.Id)!, Json), false));
            foreach (var remote in received)
            {
                var r = remote.IsDeleted ? SyncTombstone.Of(remote) : remote;
                if (!set.ApplyRemote(r)) continue;
                erasing |= r.IsDeleted;
                rows.Add(new LocalDatabase.Row(set.Kind, r.Id.ToString(), JsonSerializer.Serialize(r, Json), false));
                kinds |= set.Changes;
            }
        }

        Apply(_subjects, pushed.Subjects, incoming.Subjects);
        Apply(_topics, pushed.Topics, incoming.Topics);
        Apply(_subtopics, pushed.Subtopics, incoming.Subtopics);
        Apply(_events, pushed.Events, incoming.Events);
        Apply(_reminders, pushed.Reminders, incoming.Reminders);
        Apply(_notes, pushed.Notes, incoming.Notes);
        Apply(_sessions, pushed.FocusSessions, incoming.FocusSessions);

        if (pushed.Preferences is { } sentPrefs && _preferencesDto?.UpdatedAt == sentPrefs.UpdatedAt) _preferencesDirty = false;
        if (incoming.Preferences is { } remotePrefs &&
            (!_preferencesDirty || _preferencesDto is null || remotePrefs.UpdatedAt >= _preferencesDto.UpdatedAt))
        {
            _preferencesDto = remotePrefs;
            _preferencesDirty = false;
            _preferences = Preferences.Deserialize(remotePrefs.Json);
            kinds |= DataKinds.Preferences;
        }
        if (_preferencesDto is not null && (pushed.Preferences is not null || incoming.Preferences is not null))
            rows.Add(new LocalDatabase.Row(PreferencesKind, PreferencesId, JsonSerializer.Serialize(_preferencesDto, Json), _preferencesDirty));

        if (rows.Count > 0) Enqueue(db => db.UpsertAsync(rows));
        if (erasing) Enqueue(db => db.TruncateLogAsync());
        if (kinds != DataKinds.None) Changed?.Invoke(this, new DataChangedEventArgs(kinds, fromSync: true));
    }

    // ---------- Internals ----------

    private IEnumerable<IEntitySet> Sets => [_subjects, _topics, _subtopics, _events, _reminders, _notes, _sessions];

    private EntitySet<T> SetFor<T>() where T : SyncItemDto => (EntitySet<T>)Sets.First(s => s is EntitySet<T>);

    private void Persist(string kind, string id, string json, bool dirty) =>
        Enqueue(db => db.UpsertAsync([new LocalDatabase.Row(kind, id, json, dirty)]));

    private void Enqueue(Func<LocalDatabase, Task> write)
    {
        var db = _db;
        if (db is null || _writes is null) return;
        _writes.Writer.TryWrite(() => write(db));
    }

    private async Task DrainWritesAsync(ChannelReader<Func<Task>> reader)
    {
        await foreach (var write in reader.ReadAllAsync())
        {
            try { await write(); }
            catch (Exception ex) { logger.LogError(ex, "Local database write failed"); }
        }
    }

    private interface IEntitySet
    {
        string Kind { get; }
        void Clear();
        /// <summary>Returns the tombstone's JSON when the row was a deleted item that still held content.</summary>
        string? LoadJson(string json, bool dirty);
    }

    private sealed class EntitySet<T>(string kind, DataKinds changes) : IEntitySet where T : SyncItemDto
    {
        private readonly Dictionary<Guid, T> _items = [];
        private readonly HashSet<Guid> _dirty = [];

        public string Kind { get; } = kind;
        public DataKinds Changes { get; } = changes;
        public IEnumerable<T> Live => _items.Values.Where(x => !x.IsDeleted);

        public T? Get(Guid id) => _items.TryGetValue(id, out var x) && !x.IsDeleted ? x : null;
        public T? Raw(Guid id) => _items.GetValueOrDefault(id);

        public void Clear() { _items.Clear(); _dirty.Clear(); }

        public string? LoadJson(string json, bool dirty)
        {
            if (JsonSerializer.Deserialize<T>(json, Json) is not { } item) return null;
            if (!item.IsDeleted)
            {
                Put(item, dirty);
                return null;
            }
            var tombstone = SyncTombstone.Of(item);
            Put(tombstone, dirty);
            var erased = JsonSerializer.Serialize(tombstone, Json);
            return erased == json ? null : erased;
        }

        public void Put(T item, bool dirty)
        {
            _items[item.Id] = item;
            if (dirty) _dirty.Add(item.Id); else _dirty.Remove(item.Id);
        }

        public List<T> Dirty() => _dirty.Select(id => _items[id]).ToList();

        public bool ClearDirtyIfUnchanged(T pushed)
        {
            if (!_items.TryGetValue(pushed.Id, out var current) || current.UpdatedAt != pushed.UpdatedAt) return false;
            return _dirty.Remove(pushed.Id);
        }

        /// <summary>Returns true when the remote row was taken.</summary>
        public bool ApplyRemote(T remote)
        {
            if (_items.TryGetValue(remote.Id, out var local) && _dirty.Contains(remote.Id) && local.UpdatedAt > remote.UpdatedAt)
                return false;
            Put(remote, dirty: false);
            return true;
        }
    }
}
