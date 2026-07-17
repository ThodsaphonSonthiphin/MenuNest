using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeasonPeriodsJson",
                table: "TripPlaces",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<string>(
                name: "SeasonPeriodsJson",
                table: "PlaceProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeasonPeriodsJson",
                table: "TripPlaces");

            migrationBuilder.DropColumn(
                name: "SeasonPeriodsJson",
                table: "PlaceProfiles");
        }
    }
}
