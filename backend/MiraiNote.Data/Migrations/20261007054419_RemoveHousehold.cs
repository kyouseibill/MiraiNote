using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveHousehold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdChatDraft");

            migrationBuilder.DropTable(
                name: "HouseholdCompletionRecord");

            migrationBuilder.DropTable(
                name: "HouseholdConsumableReminder");

            migrationBuilder.DropTable(
                name: "HouseholdInvitation");

            migrationBuilder.DropTable(
                name: "HouseholdItemTemplate");

            migrationBuilder.DropTable(
                name: "HouseholdNotificationSetting");

            migrationBuilder.DropTable(
                name: "HouseholdReminderLog");

            migrationBuilder.DropTable(
                name: "HouseholdItem");

            migrationBuilder.DropTable(
                name: "HouseholdConsumable");

            migrationBuilder.DropTable(
                name: "HouseholdMember");

            migrationBuilder.DropTable(
                name: "Household");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "RemoveHousehold is irreversible; restore from backup / tag archive/household-6edd415.");
        }
    }
}
