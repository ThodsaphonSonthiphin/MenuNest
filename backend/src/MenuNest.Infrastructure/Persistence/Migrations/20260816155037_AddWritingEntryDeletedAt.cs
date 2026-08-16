using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingEntryDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "WritingEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingEntries_UserId_DeletedAt",
                table: "WritingEntries",
                columns: new[] { "UserId", "DeletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WritingEntries_UserId_DeletedAt",
                table: "WritingEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WritingEntries");
        }
    }
}
