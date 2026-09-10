using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MiraiNote.Data.Context;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MiraiNoteDbContext))]
    [Migration("20260910120000_AddChatModelSelection")]
    public partial class AddChatModelSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "AgentRun",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiProvider",
                table: "AgentRun",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "ChatSession",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiProvider",
                table: "ChatSession",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AiModel", table: "AgentRun");
            migrationBuilder.DropColumn(name: "AiProvider", table: "AgentRun");
            migrationBuilder.DropColumn(name: "AiModel", table: "ChatSession");
            migrationBuilder.DropColumn(name: "AiProvider", table: "ChatSession");
        }
    }
}
