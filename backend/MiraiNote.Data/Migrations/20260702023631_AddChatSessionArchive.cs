using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "ChatSession",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_UserId_IsArchived_UpdatedAt",
                table: "ChatSession",
                columns: new[] { "UserId", "IsArchived", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSession_UserId_IsArchived_UpdatedAt",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "ChatSession");
        }
    }
}
