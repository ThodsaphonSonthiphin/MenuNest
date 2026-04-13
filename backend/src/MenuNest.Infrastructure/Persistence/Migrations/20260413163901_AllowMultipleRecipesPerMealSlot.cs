using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleRecipesPerMealSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MealPlanEntries_FamilyId_Date_MealSlot",
                table: "MealPlanEntries");

            migrationBuilder.CreateIndex(
                name: "IX_MealPlanEntries_FamilyId_Date_MealSlot",
                table: "MealPlanEntries",
                columns: new[] { "FamilyId", "Date", "MealSlot" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MealPlanEntries_FamilyId_Date_MealSlot",
                table: "MealPlanEntries");

            migrationBuilder.CreateIndex(
                name: "IX_MealPlanEntries_FamilyId_Date_MealSlot",
                table: "MealPlanEntries",
                columns: new[] { "FamilyId", "Date", "MealSlot" },
                unique: true);
        }
    }
}
