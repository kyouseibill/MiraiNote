using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatProjectsAndConversationFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchedFromMessageId",
                table: "ChatSession",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchedFromSessionId",
                table: "ChatSession",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "ChatSession",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "ChatSession",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatProject",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatProject", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatProject_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_ProjectId",
                table: "ChatSession",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_UserId_ProjectId_IsPinned_UpdatedAt",
                table: "ChatSession",
                columns: new[] { "UserId", "ProjectId", "IsPinned", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatProject_UserId_Name",
                table: "ChatProject",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSession_ChatProject_ProjectId",
                table: "ChatSession",
                column: "ProjectId",
                principalTable: "ChatProject",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSession_ChatProject_ProjectId",
                table: "ChatSession");

            migrationBuilder.DropTable(
                name: "ChatProject");

            migrationBuilder.DropIndex(
                name: "IX_ChatSession_ProjectId",
                table: "ChatSession");

            migrationBuilder.DropIndex(
                name: "IX_ChatSession_UserId_ProjectId_IsPinned_UpdatedAt",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "BranchedFromMessageId",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "BranchedFromSessionId",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ChatSession");
        }
    }
}
