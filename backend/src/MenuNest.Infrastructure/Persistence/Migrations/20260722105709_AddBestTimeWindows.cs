using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBestTimeWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "BestTimeWindowsJson", table: "TripPlaces",
                type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'");
            migrationBuilder.AddColumn<string>(name: "BestTimeWindowsJson", table: "PlaceProfiles",
                type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'");

            // Copy each existing single window into a one-element JSON list (note = null).
            // CONVERT(...,108) → 'HH:mm:ss', which System.Text.Json parses back into TimeOnly.
            migrationBuilder.Sql(@"
                UPDATE TripPlaces
                SET BestTimeWindowsJson = '[{""start"":""' + CONVERT(varchar(8), BestTimeStart, 108)
                    + '"",""end"":""' + CONVERT(varchar(8), BestTimeEnd, 108) + '"",""note"":null}]'
                WHERE BestTimeStart IS NOT NULL AND BestTimeEnd IS NOT NULL;");
            migrationBuilder.Sql(@"
                UPDATE PlaceProfiles
                SET BestTimeWindowsJson = '[{""start"":""' + CONVERT(varchar(8), BestTimeStart, 108)
                    + '"",""end"":""' + CONVERT(varchar(8), BestTimeEnd, 108) + '"",""note"":null}]'
                WHERE BestTimeStart IS NOT NULL AND BestTimeEnd IS NOT NULL;");

            migrationBuilder.DropColumn(name: "BestTimeStart", table: "TripPlaces");
            migrationBuilder.DropColumn(name: "BestTimeEnd", table: "TripPlaces");
            migrationBuilder.DropColumn(name: "BestTimeStart", table: "PlaceProfiles");
            migrationBuilder.DropColumn(name: "BestTimeEnd", table: "PlaceProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeStart", table: "TripPlaces", type: "time", nullable: true);
            migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeEnd", table: "TripPlaces", type: "time", nullable: true);
            migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeStart", table: "PlaceProfiles", type: "time", nullable: true);
            migrationBuilder.AddColumn<TimeOnly>(name: "BestTimeEnd", table: "PlaceProfiles", type: "time", nullable: true);
            // best-effort restore: first window only
            foreach (var tbl in new[] { "TripPlaces", "PlaceProfiles" })
                migrationBuilder.Sql($@"
                    UPDATE {tbl}
                    SET BestTimeStart = CONVERT(time, JSON_VALUE(BestTimeWindowsJson, '$[0].start')),
                        BestTimeEnd   = CONVERT(time, JSON_VALUE(BestTimeWindowsJson, '$[0].end'))
                    WHERE ISJSON(BestTimeWindowsJson) = 1 AND JSON_VALUE(BestTimeWindowsJson, '$[0].start') IS NOT NULL;");
            migrationBuilder.DropColumn(name: "BestTimeWindowsJson", table: "TripPlaces");
            migrationBuilder.DropColumn(name: "BestTimeWindowsJson", table: "PlaceProfiles");
        }
    }
}