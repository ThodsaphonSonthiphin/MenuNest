using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentEnvelopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                table: "BudgetTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentForAccountId",
                table: "BudgetCategories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_FamilyId_PaymentId",
                table: "BudgetTransactions",
                columns: new[] { "FamilyId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetCategories_PaymentForAccountId",
                table: "BudgetCategories",
                column: "PaymentForAccountId",
                unique: true,
                filter: "[PaymentForAccountId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_BudgetCategories_BudgetAccounts_PaymentForAccountId",
                table: "BudgetCategories",
                column: "PaymentForAccountId",
                principalTable: "BudgetAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BudgetCategories_BudgetAccounts_PaymentForAccountId",
                table: "BudgetCategories");

            migrationBuilder.DropIndex(
                name: "IX_BudgetTransactions_FamilyId_PaymentId",
                table: "BudgetTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BudgetCategories_PaymentForAccountId",
                table: "BudgetCategories");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "BudgetTransactions");

            migrationBuilder.DropColumn(
                name: "PaymentForAccountId",
                table: "BudgetCategories");
        }
    }
}
