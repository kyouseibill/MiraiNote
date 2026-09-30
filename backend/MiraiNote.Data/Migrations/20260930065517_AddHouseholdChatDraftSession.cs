using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdChatDraftSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChatSessionId",
                table: "HouseholdChatDraft",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdChatDraft_UserId_HouseholdId_ChatSessionId",
                table: "HouseholdChatDraft",
                columns: new[] { "UserId", "HouseholdId", "ChatSessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HouseholdChatDraft_UserId_HouseholdId_ChatSessionId",
                table: "HouseholdChatDraft");

            migrationBuilder.DropColumn(
                name: "ChatSessionId",
                table: "HouseholdChatDraft");
        }
    }
}
