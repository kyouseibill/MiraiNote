using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdCompletionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "HouseholdCompletionRecord",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionFingerprint",
                table: "HouseholdCompletionRecord",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_IdempotencyKey",
                table: "HouseholdCompletionRecord",
                columns: new[] { "HouseholdItemId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_IdempotencyKey",
                table: "HouseholdCompletionRecord");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "HouseholdCompletionRecord");

            migrationBuilder.DropColumn(
                name: "SubmissionFingerprint",
                table: "HouseholdCompletionRecord");
        }
    }
}
