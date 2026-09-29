using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdConsumableReminderDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "HouseholdConsumableReminder",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "HouseholdConsumableReminder",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "HouseholdConsumableReminder",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Sent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "HouseholdConsumableReminder");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "HouseholdConsumableReminder");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "HouseholdConsumableReminder");
        }
    }
}
