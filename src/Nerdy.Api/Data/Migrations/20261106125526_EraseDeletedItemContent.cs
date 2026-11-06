using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerdy.Api.Data.Migrations
{
    /// <summary>
    /// Erases the content of items deleted before deletes were stored as tombstones (SyncTombstone), leaving the same
    /// values a tombstone has. Versions are not bumped: devices already have these rows as deleted and erase their own
    /// copies when the app opens.
    /// </summary>
    public partial class EraseDeletedItemContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE Topics SET Name = N'', ColorHex = N'#5B5BD6', DueTitle = NULL, DueAt = NULL, SortOrder = 0
                WHERE IsDeleted = 1;
                """);
            migrationBuilder.Sql("""
                UPDATE Subtopics SET TopicId = '00000000-0000-0000-0000-000000000000', Name = N'', Progress = 0, SortOrder = 0
                WHERE IsDeleted = 1;
                """);
            migrationBuilder.Sql("""
                UPDATE CalendarEvents SET Title = N'', Subtitle = NULL, TopicId = NULL,
                    StartsAt = '0001-01-01T00:00:00+00:00', EndsAt = '0001-01-01T00:00:00+00:00', IsAllDay = 0,
                    IsMilestone = 0, RepeatDays = 0, RepeatUntil = NULL, ReminderMinutes = NULL, AutoFocus = 0,
                    AutoFocusMode = N'Focus', SeriesId = NULL
                WHERE IsDeleted = 1;
                """);
            migrationBuilder.Sql("""
                UPDATE Reminders SET Title = N'', DueAt = NULL, HasDueTime = 0, TopicId = NULL, IsStarred = 0, IsDone = 0,
                    CompletedAt = NULL, Notes = NULL
                WHERE IsDeleted = 1;
                """);
            migrationBuilder.Sql("""
                UPDATE Notes SET Title = N'', TopicId = NULL, SubtopicId = NULL, BodyRtf = N'', BodyText = N'',
                    ChecklistJson = N'[]'
                WHERE IsDeleted = 1;
                """);
            migrationBuilder.Sql("""
                UPDATE FocusSessions SET TopicId = NULL, Task = N'', Mode = N'Focus', PlannedSeconds = 0, ActualSeconds = 0,
                    StartedAt = '0001-01-01T00:00:00+00:00', EndedAt = '0001-01-01T00:00:00+00:00', Outcome = N'Completed',
                    ExitReason = NULL, ExitNote = NULL
                WHERE IsDeleted = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Erased content can't be brought back.
        }
    }
}
