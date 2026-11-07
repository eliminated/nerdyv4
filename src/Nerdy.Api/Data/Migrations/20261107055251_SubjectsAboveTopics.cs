using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerdy.Api.Data.Migrations
{
    /// <summary>
    /// Adds subjects above topics, keeping every row: what were topics (Biology) become subjects, what were subtopics
    /// (Cell biology) become topics, and an empty Subtopics table holds the new third level. Links move with them:
    /// TopicId becomes SubjectId everywhere, a note's SubtopicId becomes its TopicId, and each gets an empty column for
    /// the narrower link. Row versions are kept, so clients don't download everything again: the app converts its own
    /// copy the same way (DataService, local schema 2). A topic's own progress goes, as it now comes from its subtopics.
    /// </summary>
    public partial class SubjectsAboveTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Topics → Subjects.
            migrationBuilder.DropForeignKey(name: "FK_Topics_AspNetUsers_UserId", table: "Topics");
            migrationBuilder.DropPrimaryKey(name: "PK_Topics", table: "Topics");
            migrationBuilder.RenameTable(name: "Topics", newName: "Subjects");
            migrationBuilder.RenameIndex(name: "IX_Topics_UserId_Version", table: "Subjects", newName: "IX_Subjects_UserId_Version");
            migrationBuilder.AddPrimaryKey(name: "PK_Subjects", table: "Subjects", column: "Id");
            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_AspNetUsers_UserId",
                table: "Subjects",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Subtopics → Topics, belonging to the subject their old topic became.
            migrationBuilder.DropForeignKey(name: "FK_Subtopics_AspNetUsers_UserId", table: "Subtopics");
            migrationBuilder.DropPrimaryKey(name: "PK_Subtopics", table: "Subtopics");
            migrationBuilder.RenameTable(name: "Subtopics", newName: "Topics");
            migrationBuilder.RenameColumn(name: "TopicId", table: "Topics", newName: "SubjectId");
            migrationBuilder.DropColumn(name: "Progress", table: "Topics");
            migrationBuilder.RenameIndex(name: "IX_Subtopics_UserId_Version", table: "Topics", newName: "IX_Topics_UserId_Version");
            migrationBuilder.AddPrimaryKey(name: "PK_Topics", table: "Topics", column: "Id");
            migrationBuilder.AddForeignKey(
                name: "FK_Topics_AspNetUsers_UserId",
                table: "Topics",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // The new third level.
            migrationBuilder.CreateTable(
                name: "Subtopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subtopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subtopics_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_Subtopics_UserId_Version", table: "Subtopics", columns: new[] { "UserId", "Version" });

            // Links: what pointed at a topic now points at its subject; the narrower link starts empty.
            foreach (var table in new[] { "CalendarEvents", "Reminders", "FocusSessions" })
            {
                migrationBuilder.RenameColumn(name: "TopicId", table: table, newName: "SubjectId");
                migrationBuilder.AddColumn<Guid>(name: "TopicId", table: table, type: "uniqueidentifier", nullable: true);
            }

            migrationBuilder.RenameColumn(name: "TopicId", table: "Notes", newName: "SubjectId");
            migrationBuilder.RenameColumn(name: "SubtopicId", table: "Notes", newName: "TopicId");
            migrationBuilder.AddColumn<Guid>(name: "SubtopicId", table: "Notes", type: "uniqueidentifier", nullable: true);
        }

        /// <summary>The reverse, except that subtopics (the new third level) are lost and topics come back at 0%.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SubtopicId", table: "Notes");
            migrationBuilder.RenameColumn(name: "TopicId", table: "Notes", newName: "SubtopicId");
            migrationBuilder.RenameColumn(name: "SubjectId", table: "Notes", newName: "TopicId");

            foreach (var table in new[] { "CalendarEvents", "Reminders", "FocusSessions" })
            {
                migrationBuilder.DropColumn(name: "TopicId", table: table);
                migrationBuilder.RenameColumn(name: "SubjectId", table: table, newName: "TopicId");
            }

            migrationBuilder.DropTable(name: "Subtopics");

            migrationBuilder.DropForeignKey(name: "FK_Topics_AspNetUsers_UserId", table: "Topics");
            migrationBuilder.DropPrimaryKey(name: "PK_Topics", table: "Topics");
            migrationBuilder.RenameTable(name: "Topics", newName: "Subtopics");
            migrationBuilder.RenameColumn(name: "SubjectId", table: "Subtopics", newName: "TopicId");
            migrationBuilder.AddColumn<int>(name: "Progress", table: "Subtopics", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.RenameIndex(name: "IX_Topics_UserId_Version", table: "Subtopics", newName: "IX_Subtopics_UserId_Version");
            migrationBuilder.AddPrimaryKey(name: "PK_Subtopics", table: "Subtopics", column: "Id");
            migrationBuilder.AddForeignKey(
                name: "FK_Subtopics_AspNetUsers_UserId",
                table: "Subtopics",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(name: "FK_Subjects_AspNetUsers_UserId", table: "Subjects");
            migrationBuilder.DropPrimaryKey(name: "PK_Subjects", table: "Subjects");
            migrationBuilder.RenameTable(name: "Subjects", newName: "Topics");
            migrationBuilder.RenameIndex(name: "IX_Subjects_UserId_Version", table: "Topics", newName: "IX_Topics_UserId_Version");
            migrationBuilder.AddPrimaryKey(name: "PK_Topics", table: "Topics", column: "Id");
            migrationBuilder.AddForeignKey(
                name: "FK_Topics_AspNetUsers_UserId",
                table: "Topics",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
