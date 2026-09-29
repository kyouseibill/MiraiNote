using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HouseholdConsumableReminder",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsumableId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdConsumableReminder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdConsumableReminder_HouseholdConsumable_ConsumableId",
                        column: x => x.ConsumableId,
                        principalTable: "HouseholdConsumable",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdConsumableReminder_HouseholdMember_MemberId",
                        column: x => x.MemberId,
                        principalTable: "HouseholdMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdNotificationSetting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    BarkEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    BarkAddressProtected = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    BarkAddressSuffix = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    NotificationEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PushHour = table.Column<int>(type: "int", nullable: false, defaultValue: 9),
                    PushMinute = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LeadChannel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Email"),
                    DueChannel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Bark"),
                    OverdueIntervalDays = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdNotificationSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdNotificationSetting_HouseholdMember_MemberId",
                        column: x => x.MemberId,
                        principalTable: "HouseholdMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdReminderLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdItemId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    ReminderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsCatchUp = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdReminderLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdReminderLog_HouseholdItem_HouseholdItemId",
                        column: x => x.HouseholdItemId,
                        principalTable: "HouseholdItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdReminderLog_HouseholdMember_MemberId",
                        column: x => x.MemberId,
                        principalTable: "HouseholdMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdConsumableReminder_ConsumableId_MemberId_Channel",
                table: "HouseholdConsumableReminder",
                columns: new[] { "ConsumableId", "MemberId", "Channel" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdConsumableReminder_MemberId",
                table: "HouseholdConsumableReminder",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdNotificationSetting_MemberId",
                table: "HouseholdNotificationSetting",
                column: "MemberId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdReminderLog_HouseholdItemId_MemberId_ReminderDate_Channel",
                table: "HouseholdReminderLog",
                columns: new[] { "HouseholdItemId", "MemberId", "ReminderDate", "Channel" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdReminderLog_MemberId_ReminderDate",
                table: "HouseholdReminderLog",
                columns: new[] { "MemberId", "ReminderDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdConsumableReminder");

            migrationBuilder.DropTable(
                name: "HouseholdNotificationSetting");

            migrationBuilder.DropTable(
                name: "HouseholdReminderLog");
        }
    }
}
