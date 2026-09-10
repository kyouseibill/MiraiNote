using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableAgentRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckpointJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserMessageId = table.Column<int>(type: "int", nullable: true),
                    AssistantMessageId = table.Column<int>(type: "int", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecoverableAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRun", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRun_ChatSession_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentRun_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentRunEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRunEvent_AgentRun_RunId",
                        column: x => x.RunId,
                        principalTable: "AgentRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRun_SessionId",
                table: "AgentRun",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRun_Status_CreatedAt",
                table: "AgentRun",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRun_UserId_SessionId_Status",
                table: "AgentRun",
                columns: new[] { "UserId", "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunEvent_RunId_Sequence",
                table: "AgentRunEvent",
                columns: new[] { "RunId", "Sequence" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRunEvent");

            migrationBuilder.DropTable(
                name: "AgentRun");
        }
    }
}
