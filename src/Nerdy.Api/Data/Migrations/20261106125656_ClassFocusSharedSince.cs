using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerdy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClassFocusSharedSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SharesFocusSince",
                table: "ClassMemberships",
                type: "datetimeoffset",
                nullable: true);

            // Students already sharing start from now, so no earlier history is shown to the teacher.
            migrationBuilder.Sql("UPDATE ClassMemberships SET SharesFocusSince = SYSDATETIMEOFFSET() WHERE SharesFocus = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SharesFocusSince",
                table: "ClassMemberships");
        }
    }
}
