using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ElapsedSeconds = table.Column<int>(type: "int", nullable: false),
                    WordsPerMinute = table.Column<double>(type: "float", nullable: false),
                    CorrectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TargetRule = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HitCount = table.Column<int>(type: "int", nullable: true),
                    MissCount = table.Column<int>(type: "int", nullable: true),
                    ThaiWhyLine = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SentenceCombiningItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StuckWordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingEntries_UserId_Date",
                table: "WritingEntries",
                columns: new[] { "UserId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingEntries");
        }
    }
}
