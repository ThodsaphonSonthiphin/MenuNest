using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSessions",
                columns: table => new
                {
                    RefreshCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSessions", x => x.RefreshCode);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSessions_Subject",
                table: "AppSessions",
                column: "Subject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSessions");
        }
    }
}
