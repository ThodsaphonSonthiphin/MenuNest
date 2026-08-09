using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MigrateChecklistsToStops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceChecklistEntries");

            migrationBuilder.CreateTable(
                name: "StopChecklistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChecklistItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsChecked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopChecklistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StopChecklistEntries_ChecklistItems_ChecklistItemId",
                        column: x => x.ChecklistItemId,
                        principalTable: "ChecklistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StopChecklistEntries_Stops_StopId",
                        column: x => x.StopId,
                        principalTable: "Stops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StopChecklistEntries_ChecklistItemId",
                table: "StopChecklistEntries",
                column: "ChecklistItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StopChecklistEntries_StopId_ChecklistItemId",
                table: "StopChecklistEntries",
                columns: new[] { "StopId", "ChecklistItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StopChecklistEntries");

            migrationBuilder.CreateTable(
                name: "PlaceChecklistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChecklistItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsChecked = table.Column<bool>(type: "bit", nullable: false),
                    TripPlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceChecklistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceChecklistEntries_ChecklistItems_ChecklistItemId",
                        column: x => x.ChecklistItemId,
                        principalTable: "ChecklistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceChecklistEntries_TripPlaces_TripPlaceId",
                        column: x => x.TripPlaceId,
                        principalTable: "TripPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceChecklistEntries_ChecklistItemId",
                table: "PlaceChecklistEntries",
                column: "ChecklistItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceChecklistEntries_TripPlaceId_ChecklistItemId",
                table: "PlaceChecklistEntries",
                columns: new[] { "TripPlaceId", "ChecklistItemId" },
                unique: true);
        }
    }
}
