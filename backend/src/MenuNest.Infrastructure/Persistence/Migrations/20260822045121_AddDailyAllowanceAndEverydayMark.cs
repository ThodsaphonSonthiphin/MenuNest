using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyAllowanceAndEverydayMark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // menunest-188: the old budget data is no longer valid. Every Account's
            // opening money is a stored number that no BudgetTransaction explains, and
            // a surviving Envelope holding unexplained Available money would corrupt
            // Ready to Assign the same way. Wipe it; there is no back-fill.
            migrationBuilder.Sql("DELETE FROM BudgetTransactions;");
            migrationBuilder.Sql("DELETE FROM MonthlyAssignments;");
            migrationBuilder.Sql("DELETE FROM BudgetCategories;");
            migrationBuilder.Sql("DELETE FROM BudgetCategoryGroups;");
            migrationBuilder.Sql("DELETE FROM BudgetAccounts;");

            migrationBuilder.AddColumn<bool>(
                name: "IsEveryday",
                table: "BudgetCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DailyAllowances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    FrozenPot = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    FrozenOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ForYear = table.Column<int>(type: "int", nullable: false),
                    ForMonth = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyAllowances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyAllowances_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyAllowances_FamilyId",
                table: "DailyAllowances",
                column: "FamilyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyAllowances");

            migrationBuilder.DropColumn(
                name: "IsEveryday",
                table: "BudgetCategories");
        }
    }
}
