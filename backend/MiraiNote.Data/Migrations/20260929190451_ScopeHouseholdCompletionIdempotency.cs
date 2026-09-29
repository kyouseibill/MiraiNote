using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeHouseholdCompletionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_IdempotencyKey",
                table: "HouseholdCompletionRecord");

            migrationBuilder.AddColumn<int>(
                name: "IdempotencyUserId",
                table: "HouseholdCompletionRecord",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestBodyHash",
                table: "HouseholdCompletionRecord",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_IdempotencyUserId_IdempotencyKey",
                table: "HouseholdCompletionRecord",
                columns: new[] { "HouseholdItemId", "IdempotencyUserId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_IdempotencyUserId_IdempotencyKey",
                table: "HouseholdCompletionRecord");

            migrationBuilder.DropColumn(
                name: "IdempotencyUserId",
                table: "HouseholdCompletionRecord");

            migrationBuilder.DropColumn(
                name: "RequestBodyHash",
                table: "HouseholdCompletionRecord");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_IdempotencyKey",
                table: "HouseholdCompletionRecord",
                columns: new[] { "HouseholdItemId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");
        }
    }
}
