using Microsoft.Data.Sqlite;

namespace Nerdy.App.Services.Data;

/// <summary>
/// The per-account SQLite file. Every synced entity is stored as its JSON DTO in one table keyed by
/// (kind, id) with a dirty flag, plus a small key/value table for sync state and caches. Keeping rows as
/// JSON means new DTO fields need no local migrations. Writes are serialised on a background queue.
/// </summary>
public sealed class LocalDatabase : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LocalDatabase(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = true }.ToString();
    }

    public static async Task<LocalDatabase> OpenAsync(string path)
    {
        var db = new LocalDatabase(path);
        await db.ExecuteAsync("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS items (
                kind  TEXT NOT NULL,
                id    TEXT NOT NULL,
                json  TEXT NOT NULL,
                dirty INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (kind, id));
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL);
            """);
        return db;
    }

    public sealed record Row(string Kind, string Id, string Json, bool Dirty);

    public async Task<List<Row>> LoadAllAsync()
    {
        var rows = new List<Row>();
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT kind, id, json, dirty FROM items";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Row(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0));
        return rows;
    }

    public Task UpsertAsync(IReadOnlyCollection<Row> rows) => WriteAsync(async (conn, tx) =>
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO items (kind, id, json, dirty) VALUES ($k, $i, $j, $d)
            ON CONFLICT(kind, id) DO UPDATE SET json = excluded.json, dirty = excluded.dirty
            """;
        var k = cmd.Parameters.Add("$k", SqliteType.Text);
        var i = cmd.Parameters.Add("$i", SqliteType.Text);
        var j = cmd.Parameters.Add("$j", SqliteType.Text);
        var d = cmd.Parameters.Add("$d", SqliteType.Integer);
        foreach (var row in rows)
        {
            k.Value = row.Kind; i.Value = row.Id; j.Value = row.Json; d.Value = row.Dirty ? 1 : 0;
            await cmd.ExecuteNonQueryAsync();
        }
    });

    /// <summary>
    /// Replaces every item row and sets the given meta values in one transaction, so an upgrade of the stored format
    /// either happens completely or not at all.
    /// </summary>
    public Task ReplaceAllAsync(IReadOnlyCollection<Row> rows, IReadOnlyDictionary<string, string?> meta) => WriteAsync(async (conn, tx) =>
    {
        await using (var clear = conn.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM items";
            await clear.ExecuteNonQueryAsync();
        }

        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO items (kind, id, json, dirty) VALUES ($k, $i, $j, $d)";
            var k = insert.Parameters.Add("$k", SqliteType.Text);
            var i = insert.Parameters.Add("$i", SqliteType.Text);
            var j = insert.Parameters.Add("$j", SqliteType.Text);
            var d = insert.Parameters.Add("$d", SqliteType.Integer);
            foreach (var row in rows)
            {
                k.Value = row.Kind; i.Value = row.Id; j.Value = row.Json; d.Value = row.Dirty ? 1 : 0;
                await insert.ExecuteNonQueryAsync();
            }
        }

        foreach (var (key, value) in meta)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = value is null
                ? "DELETE FROM meta WHERE key = $k"
                : "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            cmd.Parameters.AddWithValue("$k", key);
            if (value is not null) cmd.Parameters.AddWithValue("$v", value);
            await cmd.ExecuteNonQueryAsync();
        }
    });

    public async Task<string?> GetMetaAsync(string key)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return await cmd.ExecuteScalarAsync() as string;
    }

    public Task SetMetaAsync(string key, string? value) => WriteAsync(async (conn, tx) =>
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        if (value is null)
        {
            cmd.CommandText = "DELETE FROM meta WHERE key = $k";
        }
        else
        {
            cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            cmd.Parameters.AddWithValue("$v", value);
        }
        cmd.Parameters.AddWithValue("$k", key);
        await cmd.ExecuteNonQueryAsync();
    });

    /// <summary>
    /// Moves the write-ahead log into the database file and empties it. Run after erasing content: the log still holds
    /// the pages as they were before, which secure_delete only overwrites in the database file itself.
    /// </summary>
    public async Task TruncateLogAsync()
    {
        await _gate.WaitAsync();
        try { await ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE)"); }
        finally { _gate.Release(); }
    }

    private async Task WriteAsync(Func<SqliteConnection, SqliteTransaction, Task> work)
    {
        await _gate.WaitAsync();
        try
        {
            await using var conn = await OpenConnectionAsync();
            await using var tx = conn.BeginTransaction();
            await work(conn, tx);
            await tx.CommitAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        // Overwrite replaced and deleted content in the file instead of leaving it in free pages.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA secure_delete = ON";
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    public async ValueTask DisposeAsync()
    {
        // Wait for queued writes to finish before letting go of the file.
        await _gate.WaitAsync();
        _gate.Release();
        SqliteConnection.ClearAllPools();
    }
}
