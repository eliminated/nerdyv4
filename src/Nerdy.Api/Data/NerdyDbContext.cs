using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nerdy.Api.Data.Entities;
using Nerdy.Contracts;

namespace Nerdy.Api.Data;

public sealed class NerdyDbContext(DbContextOptions<NerdyDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Subtopic> Subtopics => Set<Subtopic>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<FocusSession> FocusSessions => Set<FocusSession>();
    public DbSet<UserPreferences> Preferences => Set<UserPreferences>();
    public DbSet<UserSyncState> SyncStates => Set<UserSyncState>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExternalLoginCode> ExternalLoginCodes => Set<ExternalLoginCode>();

    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<ClassMembership> ClassMemberships => Set<ClassMembership>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentCompletion> AssignmentCompletions => Set<AssignmentCompletion>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name: readable, and safe when values are added to Nerdy.Contracts.
        configurationBuilder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ClassRole>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<FocusMode>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<FocusOutcome>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ExitReason>().HaveConversion<string>().HaveMaxLength(20);

        // SQLite has no DateTimeOffset type, so EF cannot compare or sort such columns there. Store UTC ticks
        // instead (lossless for instants; offsets are not preserved, and the API only deals in instants).
        // SQL Server keeps its native datetimeoffset, which is what the migrations describe.
        if (Database.IsSqlite())
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(NerdyDbContext).Assembly);
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
}
