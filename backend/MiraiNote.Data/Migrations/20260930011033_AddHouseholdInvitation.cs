using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdInvitation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HouseholdInvitation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    InviterUserId = table.Column<int>(type: "int", nullable: false),
                    InviteeUserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdInvitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdInvitation_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdInvitation_HouseholdId_InviteeUserId",
                table: "HouseholdInvitation",
                columns: new[] { "HouseholdId", "InviteeUserId" },
                unique: true,
                filter: "[Status] = 'Pending' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdInvitation_InviteeUserId_Status_ExpiresAt",
                table: "HouseholdInvitation",
                columns: new[] { "InviteeUserId", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdInvitation");
        }
    }
}
