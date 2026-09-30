using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdNotificationFailureAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BarkFailureAcknowledgedAt",
                table: "HouseholdNotificationSetting",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailFailureAcknowledgedAt",
                table: "HouseholdNotificationSetting",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BarkFailureAcknowledgedAt",
                table: "HouseholdNotificationSetting");

            migrationBuilder.DropColumn(
                name: "EmailFailureAcknowledgedAt",
                table: "HouseholdNotificationSetting");
        }
    }
}
