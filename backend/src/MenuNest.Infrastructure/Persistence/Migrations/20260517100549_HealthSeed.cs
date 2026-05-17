using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuNest.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Seeds 20 common Symptoms and 15 common Triggers as <c>IsSeed = true</c>,
    /// <c>UserId = null</c> (global, shared across all users). Custom user-added
    /// Symptoms/Triggers are stored alongside with <c>UserId</c> populated.
    ///
    /// Fixed Guids are used for idempotency — re-running the migration on a
    /// fresh DB produces identical IDs, and rolling back removes only the
    /// rows this migration inserted.
    /// </summary>
    public partial class HealthSeed : Migration
    {
        private static readonly DateTime SeedTimestamp =
            new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Symptoms",
                columns: new[] { "Id", "Name", "IsSeed", "UserId", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { Guid.Parse("11111111-0001-0000-0000-000000000001"), "ปวดหัว",         true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000002"), "ไมเกรน",         true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000003"), "ปวดท้อง",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000004"), "ปวดประจำเดือน", true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000005"), "ไข้",             true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000006"), "ไอ",              true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000007"), "จาม",             true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000008"), "ปวดเมื่อย",      true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000009"), "ปวดข้อ",          true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000010"), "ปวดหลัง",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000011"), "ปวดคอ",          true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000012"), "คลื่นไส้",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000013"), "อาเจียน",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000014"), "ท้องเสีย",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000015"), "ท้องผูก",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000016"), "นอนไม่หลับ",    true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000017"), "หน้ามืด",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000018"), "เหนื่อย",        true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000019"), "หายใจไม่ออก",   true, null, SeedTimestamp, null },
                    { Guid.Parse("11111111-0001-0000-0000-000000000020"), "ปวดฟัน",         true, null, SeedTimestamp, null }
                });

            migrationBuilder.InsertData(
                table: "Triggers",
                columns: new[] { "Id", "Name", "IsSeed", "UserId", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { Guid.Parse("22222222-0001-0000-0000-000000000001"), "เครียด",         true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000002"), "นอนน้อย",        true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000003"), "ฮอร์โมน",        true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000004"), "อาหาร",          true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000005"), "อากาศ",          true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000006"), "แสงจ้า",          true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000007"), "เสียงดัง",       true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000008"), "กลิ่นแรง",       true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000009"), "ออกกำลังกาย",    true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000010"), "ข้ามมื้ออาหาร",  true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000011"), "คาเฟอีน",        true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000012"), "แอลกอฮอล์",     true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000013"), "เพศสัมพันธ์",    true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000014"), "หน้าจอ",         true, null, SeedTimestamp, null },
                    { Guid.Parse("22222222-0001-0000-0000-000000000015"), "การเดินทาง",     true, null, SeedTimestamp, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Delete only the rows we inserted — user-custom rows
            // (UserId IS NOT NULL) are left untouched.
            migrationBuilder.Sql(
                "DELETE FROM [Symptoms] WHERE [Id] LIKE '11111111-0001-%' AND [IsSeed] = 1;");
            migrationBuilder.Sql(
                "DELETE FROM [Triggers] WHERE [Id] LIKE '22222222-0001-%' AND [IsSeed] = 1;");
        }
    }
}
