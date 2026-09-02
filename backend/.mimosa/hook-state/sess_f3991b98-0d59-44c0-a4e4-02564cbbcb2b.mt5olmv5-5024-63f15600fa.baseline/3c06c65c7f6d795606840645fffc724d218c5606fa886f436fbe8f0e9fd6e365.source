using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_UserId_UpdatedAt",
                table: "ChatSession",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_SessionId_CreatedAt",
                table: "ChatMessage",
                columns: new[] { "SessionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSession_UserId_UpdatedAt",
                table: "ChatSession");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_SessionId_CreatedAt",
                table: "ChatMessage");
        }
    }
}
