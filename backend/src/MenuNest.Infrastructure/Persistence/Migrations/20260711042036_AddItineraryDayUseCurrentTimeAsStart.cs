using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItineraryDayUseCurrentTimeAsStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseCurrentTimeAsStart",
                table: "ItineraryDays",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseCurrentTimeAsStart",
                table: "ItineraryDays");
        }
    }
}
