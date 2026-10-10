using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Sync;

/// <summary>
/// The sync protocol from docs/ARCHITECTURE.md, in one transaction:
/// 1. bump the user's version once (optimistic concurrency on <see cref="UserSyncState.Version"/>) → v;
/// 2. upsert each pushed row by last-write-wins on UpdatedAt (ties go to the client), stamping applied rows with v;
///    rows that are older than the server copy, or whose id belongs to another user, are rejected;
/// 3. return every row with Version &gt; SinceVersion plus the server copy of each rejected row of this user.
/// </summary>
public sealed class SyncService(NerdyDbContext db, TimeProvider time, ILogger<SyncService> logger)
{
    private const int MaxAttempts = 3;
    /// <summary>Keeps IN (...) lists well below SQL Server's 2,100-parameter limit.</summary>
    private const int LookupBatchSize = 500;

    private static readonly SyncedType<Topic, TopicDto> Topics = new(db => db.Topics, SyncMappings.Apply, SyncMappings.ToDto);
    private static readonly SyncedType<Subtopic, SubtopicDto> Subtopics = new(db => db.Subtopics, SyncMappings.Apply, SyncMappings.ToDto);
    private static readonly SyncedType<CalendarEvent, CalendarEventDto> Events = new(db => db.CalendarEvents, SyncMappings.Apply, SyncMappings.ToDto);
    private static readonly SyncedType<Reminder, ReminderDto> Reminders = new(db => db.Reminders, SyncMappings.Apply, SyncMappings.ToDto);
    private static readonly SyncedType<Note, NoteDto> Notes = new(db => db.Notes, SyncMappings.Apply, SyncMappings.ToDto);
    private static readonly SyncedType<FocusSession, FocusSessionDto> FocusSessions = new(db => db.FocusSessions, SyncMappings.Apply, SyncMappings.ToDto);

    /// <summary>Returns null when concurrent syncs of the same account kept conflicting (→ 409, the client retries).</summary>
    public async Task<SyncResponse?> SyncAsync(Guid userId, SyncRequest request, CancellationToken cancellationToken)
    {
        // The execution strategy retries transient SQL failures (it must own the transaction); the loop around it
        // retries version conflicts with a concurrent sync of the same user, which rolled this attempt back.
        var strategy = db.Database.CreateExecutionStrategy();
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await strategy.ExecuteAsync(() => RunAsync(userId, request, cancellationToken));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "Sync of user {UserId} failed to save, most likely racing a concurrent sync (attempt {Attempt})", userId, attempt);
            }
        }

        return null;
    }

    private async Task<SyncResponse> RunAsync(Guid userId, SyncRequest request, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 1. Claim the next version first: the conditional UPDATE also serializes concurrent syncs of this user.
        var state = await db.SyncStates.SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (state is null)
        {
            state = new UserSyncState { UserId = userId };
            db.SyncStates.Add(state);
        }

        var previousVersion = state.Version;
        var version = ++state.Version;
        await db.SaveChangesAsync(cancellationToken);

        // A client that is ahead of the server (e.g. after a database restore) gets a full copy.
        var since = request.SinceVersion > previousVersion ? 0 : request.SinceVersion;

        // 2. Merge.
        var changes = request.Changes;
        var context = new MergeContext(db, userId, version, cancellationToken);
        var rejectedTopics = await context.MergeAsync(Topics, changes.Topics);
        var rejectedSubtopics = await context.MergeAsync(Subtopics, changes.Subtopics);
        var rejectedEvents = await context.MergeAsync(Events, changes.Events);
        var rejectedReminders = await context.MergeAsync(Reminders, changes.Reminders);
        var rejectedNotes = await context.MergeAsync(Notes, changes.Notes);
        var rejectedSessions = await context.MergeAsync(FocusSessions, changes.FocusSessions);
        var (preferences, preferencesRejected) = await MergePreferencesAsync(userId, changes.Preferences, version, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // 3. Pull.
        var response = new SyncResponse
        {
            Version = version,
            ServerTime = time.GetUtcNow(),
            Changes = new SyncChanges
            {
                Topics = await context.PullAsync(Topics, since, rejectedTopics),
                Subtopics = await context.PullAsync(Subtopics, since, rejectedSubtopics),
                Events = await context.PullAsync(Events, since, rejectedEvents),
                Reminders = await context.PullAsync(Reminders, since, rejectedReminders),
                Notes = await context.PullAsync(Notes, since, rejectedNotes),
                FocusSessions = await context.PullAsync(FocusSessions, since, rejectedSessions),
                Preferences = preferences is not null && (preferences.Version > since || preferencesRejected)
                    ? SyncMappings.ToDto(preferences)
                    : null,
            },
        };

        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private async Task<(UserPreferences? Row, bool Rejected)> MergePreferencesAsync(
        Guid userId, PreferencesDto? incoming, long version, CancellationToken cancellationToken)
    {
        var row = await db.Preferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (incoming is null)
        {
            return (row, false);
        }

        if (row is not null && incoming.UpdatedAt < row.UpdatedAt)
        {
            return (row, true);
        }

        if (row is null)
        {
            row = new UserPreferences { UserId = userId };
            db.Preferences.Add(row);
        }

        row.Json = incoming.Json;
        row.UpdatedAt = incoming.UpdatedAt;
        row.Version = version;
        return (row, false);
    }

    /// <summary>How one synced entity type is stored and mapped.</summary>
    private sealed record SyncedType<TEntity, TDto>(
        Func<NerdyDbContext, DbSet<TEntity>> Set,
        Action<TEntity, TDto> Apply,
        Func<TEntity, TDto> ToDto)
        where TEntity : SyncEntity, new()
        where TDto : SyncItemDto;

    private sealed record MergeContext(NerdyDbContext Db, Guid UserId, long Version, CancellationToken CancellationToken)
    {
        /// <summary>Upserts the pushed rows; returns the server copies of this user's rows that won over the push.</summary>
        public async Task<List<TDto>> MergeAsync<TEntity, TDto>(SyncedType<TEntity, TDto> type, List<TDto> incoming)
            where TEntity : SyncEntity, new()
            where TDto : SyncItemDto
        {
            var rejected = new List<TDto>();
            if (incoming.Count == 0)
            {
                return rejected;
            }

            var set = type.Set(Db);
            var existing = new Dictionary<Guid, TEntity>();
            foreach (var batch in incoming.Select(d => d.Id).Chunk(LookupBatchSize))
            {
                foreach (var row in await set.Where(e => batch.Contains(e.Id)).ToListAsync(CancellationToken))
                {
                    existing[row.Id] = row;
                }
            }

            foreach (var dto in incoming)
            {
                if (!existing.TryGetValue(dto.Id, out var row))
                {
                    row = new TEntity { Id = dto.Id, UserId = UserId };
                    set.Add(row);
                }
                else if (row.UserId != UserId)
                {
                    continue; // Someone else's id: rejected, and their row is never revealed.
                }
                else if (dto.UpdatedAt < row.UpdatedAt)
                {
                    rejected.Add(type.ToDto(row));
                    continue;
                }

                type.Apply(row, dto);
                row.Version = Version;
            }

            return rejected;
        }

        public async Task<List<TDto>> PullAsync<TEntity, TDto>(SyncedType<TEntity, TDto> type, long since, List<TDto> rejected)
            where TEntity : SyncEntity, new()
            where TDto : SyncItemDto
        {
            var changed = await type.Set(Db)
                .AsNoTracking()
                .Where(e => e.UserId == UserId && e.Version > since)
                .ToListAsync(CancellationToken);

            var result = changed.Select(type.ToDto).ToList();
            var included = changed.Select(e => e.Id).ToHashSet();
            result.AddRange(rejected.Where(r => !included.Contains(r.Id)));
            return result;
        }
    }
}
