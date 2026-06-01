using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoReminderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailReminderSent",
                table: "Memo",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PopupAcknowledged",
                table: "Memo",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "RemindMethods",
                table: "Memo",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemindedAt",
                table: "Memo",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailReminderSent",
                table: "Memo");

            migrationBuilder.DropColumn(
                name: "PopupAcknowledged",
                table: "Memo");

            migrationBuilder.DropColumn(
                name: "RemindMethods",
                table: "Memo");

            migrationBuilder.DropColumn(
                name: "RemindedAt",
                table: "Memo");
        }
    }
}
