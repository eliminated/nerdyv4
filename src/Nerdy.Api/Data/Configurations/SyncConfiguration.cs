using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerdy.Api.Data.Entities;

namespace Nerdy.Api.Data.Configurations;

/// <summary>Shared mapping of every synced table: client-generated key, owner cascade, (UserId, Version) index for pulls.</summary>
internal abstract class SyncEntityConfiguration<T> : IEntityTypeConfiguration<T> where T : SyncEntity
{
    public void Configure(EntityTypeBuilder<T> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(e => new { e.UserId, e.Version });
        ConfigureFields(entity);
    }

    protected abstract void ConfigureFields(EntityTypeBuilder<T> entity);
}

internal sealed class SubjectConfiguration : SyncEntityConfiguration<Subject>
{
    protected override void ConfigureFields(EntityTypeBuilder<Subject> subject)
    {
        subject.Property(s => s.Name).HasMaxLength(FieldLimits.Name);
        subject.Property(s => s.ColorHex).HasMaxLength(FieldLimits.ColorHex);
        subject.Property(s => s.DueTitle).HasMaxLength(FieldLimits.Name);
    }
}

internal sealed class TopicConfiguration : SyncEntityConfiguration<Topic>
{
    protected override void ConfigureFields(EntityTypeBuilder<Topic> topic) =>
        topic.Property(t => t.Name).HasMaxLength(FieldLimits.Name);
}

internal sealed class SubtopicConfiguration : SyncEntityConfiguration<Subtopic>
{
    protected override void ConfigureFields(EntityTypeBuilder<Subtopic> subtopic) =>
        subtopic.Property(s => s.Name).HasMaxLength(FieldLimits.Name);
}

internal sealed class CalendarEventConfiguration : SyncEntityConfiguration<CalendarEvent>
{
    protected override void ConfigureFields(EntityTypeBuilder<CalendarEvent> calendarEvent)
    {
        calendarEvent.Property(e => e.Title).HasMaxLength(FieldLimits.Name);
        calendarEvent.Property(e => e.Subtitle).HasMaxLength(FieldLimits.Name);
    }
}

internal sealed class ReminderConfiguration : SyncEntityConfiguration<Reminder>
{
    protected override void ConfigureFields(EntityTypeBuilder<Reminder> reminder) =>
        reminder.Property(r => r.Title).HasMaxLength(FieldLimits.Name);
}

internal sealed class NoteConfiguration : SyncEntityConfiguration<Note>
{
    protected override void ConfigureFields(EntityTypeBuilder<Note> note) =>
        note.Property(n => n.Title).HasMaxLength(FieldLimits.Name);
}

internal sealed class FocusSessionConfiguration : SyncEntityConfiguration<FocusSession>
{
    protected override void ConfigureFields(EntityTypeBuilder<FocusSession> session)
    {
        session.Property(s => s.Task).HasMaxLength(FieldLimits.Name);
        session.Property(s => s.ExitNote).HasMaxLength(FieldLimits.FocusExitNote);
        // Class progress reads a student's sessions by start time.
        session.HasIndex(s => new { s.UserId, s.StartedAt });
    }
}

internal sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> preferences)
    {
        preferences.HasKey(p => p.UserId);
        preferences.HasOne<AppUser>().WithOne().HasForeignKey<UserPreferences>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserSyncStateConfiguration : IEntityTypeConfiguration<UserSyncState>
{
    public void Configure(EntityTypeBuilder<UserSyncState> state)
    {
        state.HasKey(s => s.UserId);
        state.Property(s => s.Version).IsConcurrencyToken();
        state.HasOne<AppUser>().WithOne().HasForeignKey<UserSyncState>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
