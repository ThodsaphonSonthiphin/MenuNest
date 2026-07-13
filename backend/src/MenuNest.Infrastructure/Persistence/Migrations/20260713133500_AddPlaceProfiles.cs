using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaceProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GooglePlaceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    BestTimeStart = table.Column<TimeOnly>(type: "time", nullable: true),
                    BestTimeEnd = table.Column<TimeOnly>(type: "time", nullable: true),
                    ReviewLinksJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceProfileChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChecklistItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceProfileChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceProfileChecklistItems_ChecklistItems_ChecklistItemId",
                        column: x => x.ChecklistItemId,
                        principalTable: "ChecklistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceProfileChecklistItems_PlaceProfiles_PlaceProfileId",
                        column: x => x.PlaceProfileId,
                        principalTable: "PlaceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceProfileChecklistItems_ChecklistItemId",
                table: "PlaceProfileChecklistItems",
                column: "ChecklistItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceProfileChecklistItems_PlaceProfileId_ChecklistItemId",
                table: "PlaceProfileChecklistItems",
                columns: new[] { "PlaceProfileId", "ChecklistItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceProfiles_UserId_GooglePlaceId",
                table: "PlaceProfiles",
                columns: new[] { "UserId", "GooglePlaceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceProfileChecklistItems");

            migrationBuilder.DropTable(
                name: "PlaceProfiles");
        }
    }
}
