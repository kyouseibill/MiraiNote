using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdReminderDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "HouseholdReminderLog",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "HouseholdReminderLog",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "HouseholdReminderLog",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "HouseholdReminderLog",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Sent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "HouseholdReminderLog");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "HouseholdReminderLog");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "HouseholdReminderLog");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "HouseholdReminderLog");
        }
    }
}
