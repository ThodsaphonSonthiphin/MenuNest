using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyHead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HeadUserId",
                table: "Families",
                type: "uniqueidentifier",
                nullable: true);

            // menunest-201 rule 5 — every EXISTING family needs a head, or the
            // role would only ever exist for families created after this ships.
            // The creator if they are still a member, otherwise the
            // earliest-joined current member. A family with no members at all
            // keeps NULL, which is the correct headless state.
            //
            // T-SQL only, which is safe: nothing runs migrations except the
            // by-hand `dotnet ef database update` against Azure SQL. The app
            // does not migrate on start-up and the tests use EnsureCreated.
            migrationBuilder.Sql(@"
UPDATE f
SET f.HeadUserId = COALESCE(
    (SELECT u.Id FROM Users u WHERE u.Id = f.CreatedByUserId AND u.FamilyId = f.Id),
    (SELECT TOP 1 u2.Id FROM Users u2 WHERE u2.FamilyId = f.Id ORDER BY u2.JoinedAt)
)
FROM Families f;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeadUserId",
                table: "Families");
        }
    }
}
