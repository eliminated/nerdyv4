using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Nerdy.Api.Data;

public enum DatabaseProvider { Sqlite, SqlServer }

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary><c>Sqlite</c> for zero-setup development and tests, <c>SqlServer</c> for Azure SQL / Docker.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
}

public static class DatabaseSetup
{
    public const string ConnectionStringName = "Nerdy";

    public static IServiceCollection AddNerdyDatabase(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.Section);

        services.AddDbContext<NerdyDbContext>((sp, options) =>
        {
            var provider = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider;
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            }

            if (provider == DatabaseProvider.Sqlite)
            {
                var contentRoot = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
                options.UseSqlite(ResolveSqlitePath(connectionString, contentRoot));
            }
            else
            {
                // Azure SQL serverless resumes from auto-pause in about a minute; the default retry policy covers it.
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            }
        }, optionsLifetime: ServiceLifetime.Singleton);

        services.AddHealthChecks().AddDbContextCheck<NerdyDbContext>("database");
        return services;
    }

    /// <summary>
    /// Creates the SQLite schema (development and tests) or applies EF migrations (SQL Server).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NerdyDbContext>();

        if (db.Database.IsSqlite())
        {
            var connectionString = db.Database.GetConnectionString()!;
            if (!IsInMemory(connectionString))
            {
                var directory = Path.GetDirectoryName(new SqliteConnectionStringBuilder(connectionString).DataSource);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.MigrateAsync();
        }
    }

    /// <summary>Relative SQLite file paths are resolved against the content root, not the working directory.</summary>
    private static string ResolveSqlitePath(string connectionString, string contentRoot)
    {
        if (IsInMemory(connectionString))
        {
            return connectionString;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(contentRoot, builder.DataSource);
        }

        return builder.ToString();
    }

    private static bool IsInMemory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        return builder.Mode == SqliteOpenMode.Memory || builder.DataSource is "" or ":memory:";
    }
}
