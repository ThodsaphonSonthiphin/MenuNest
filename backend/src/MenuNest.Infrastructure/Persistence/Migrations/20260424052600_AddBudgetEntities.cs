using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetAccounts_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetCategoryGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetCategoryGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetCategoryGroups_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyIncomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyIncomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyIncomes_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Emoji = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    TargetDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetDayOfMonth = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetCategories_BudgetCategoryGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "BudgetCategoryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetCategories_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_BudgetAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BudgetAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_BudgetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "BudgetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    AssignedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyAssignments_BudgetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "BudgetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonthlyAssignments_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAccounts_FamilyId_SortOrder",
                table: "BudgetAccounts",
                columns: new[] { "FamilyId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetCategories_FamilyId_GroupId_SortOrder",
                table: "BudgetCategories",
                columns: new[] { "FamilyId", "GroupId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetCategories_GroupId",
                table: "BudgetCategories",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetCategoryGroups_FamilyId_SortOrder",
                table: "BudgetCategoryGroups",
                columns: new[] { "FamilyId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_AccountId",
                table: "BudgetTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_CategoryId",
                table: "BudgetTransactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_FamilyId_CategoryId_Date",
                table: "BudgetTransactions",
                columns: new[] { "FamilyId", "CategoryId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_FamilyId_Date",
                table: "BudgetTransactions",
                columns: new[] { "FamilyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyAssignments_CategoryId",
                table: "MonthlyAssignments",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyAssignments_FamilyId_CategoryId_Year_Month",
                table: "MonthlyAssignments",
                columns: new[] { "FamilyId", "CategoryId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyIncomes_FamilyId_Year_Month",
                table: "MonthlyIncomes",
                columns: new[] { "FamilyId", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetTransactions");

            migrationBuilder.DropTable(
                name: "MonthlyAssignments");

            migrationBuilder.DropTable(
                name: "MonthlyIncomes");

            migrationBuilder.DropTable(
                name: "BudgetAccounts");

            migrationBuilder.DropTable(
                name: "BudgetCategories");

            migrationBuilder.DropTable(
                name: "BudgetCategoryGroups");
        }
    }
}
