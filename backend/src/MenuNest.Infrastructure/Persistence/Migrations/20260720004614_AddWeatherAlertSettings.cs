using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeatherAlertSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FeelsLikeWarnThreshold",
                table: "UserSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UvWarnThreshold",
                table: "UserSettings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeelsLikeWarnThreshold",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "UvWarnThreshold",
                table: "UserSettings");
        }
    }
}
