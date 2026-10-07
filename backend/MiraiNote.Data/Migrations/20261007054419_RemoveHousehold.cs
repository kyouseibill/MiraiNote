using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

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

            migrationBuilder.DeleteData(
                table: "HouseholdItemTemplate",
                keyColumn: "Id",
                keyValues: new object[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 });

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
            migrationBuilder.CreateTable(
                name: "Household",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Household", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdChatDraft",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CandidateItemIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ChatSessionId = table.Column<int>(type: "int", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    StoredCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    StoredCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StoredItemId = table.Column<int>(type: "int", nullable: true),
                    StoredSkipDeduction = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdChatDraft", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdItemTemplate",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CycleUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CycleValue = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdItemTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdConsumable",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CurrentStock = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LowStockReminderSent = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PurchaseLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RestockThreshold = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    SpecModel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdConsumable", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdConsumable_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdInvitation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InviteeUserId = table.Column<int>(type: "int", nullable: false),
                    InviterUserId = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
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

            migrationBuilder.CreateTable(
                name: "HouseholdMember",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    NotifyFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdMember_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdMember_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdConsumableReminder",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsumableId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Sent"),
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
                name: "HouseholdItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssigneeMemberId = table.Column<int>(type: "int", nullable: true),
                    ConsumableId = table.Column<int>(type: "int", nullable: true),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    AliasesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CycleUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CycleValue = table.Column<int>(type: "int", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastDoneDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LeadDays = table.Column<int>(type: "int", nullable: false, defaultValue: 7),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MileageCycleKm = table.Column<int>(type: "int", nullable: true),
                    ModelSpec = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NextDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PurchaseLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdItem_HouseholdConsumable_ConsumableId",
                        column: x => x.ConsumableId,
                        principalTable: "HouseholdConsumable",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdItem_HouseholdMember_AssigneeMemberId",
                        column: x => x.AssigneeMemberId,
                        principalTable: "HouseholdMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdItem_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
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
                    BarkAddressProtected = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    BarkAddressSuffix = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    BarkEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    BarkFailureAcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    DueChannel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Bark"),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EmailFailureAcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LeadChannel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Email"),
                    NotificationEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OverdueIntervalDays = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    PushHour = table.Column<int>(type: "int", nullable: false, defaultValue: 9),
                    PushMinute = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                name: "HouseholdCompletionRecord",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompletedByMemberId = table.Column<int>(type: "int", nullable: true),
                    HouseholdItemId = table.Column<int>(type: "int", nullable: false),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: false),
                    CompletedByUsername = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ConsumableId = table.Column<int>(type: "int", nullable: true),
                    ConsumableQuantityDeducted = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IdempotencyUserId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    NeedsRestock = table.Column<bool>(type: "bit", nullable: false),
                    NewExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PhotoRefs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PurchaseLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestBodyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubmissionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdCompletionRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdCompletionRecord_HouseholdItem_HouseholdItemId",
                        column: x => x.HouseholdItemId,
                        principalTable: "HouseholdItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseholdCompletionRecord_HouseholdMember_CompletedByMemberId",
                        column: x => x.CompletedByMemberId,
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
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    IsCatchUp = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReminderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Sent"),
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

            migrationBuilder.InsertData(
                table: "HouseholdItemTemplate",
                columns: new[] { "Id", "Category", "CreatedAt", "CreatedBy", "CycleUnit", "CycleValue", "IsDeleted", "ItemType", "Name", "SortOrder", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 1, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 3, false, "Recurring", "空调滤网", 1, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 2, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 6, false, "Recurring", "净水器 PP 棉", 2, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 3, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 12, false, "Recurring", "净水器活性炭", 3, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 4, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 24, false, "Recurring", "净水器 RO 膜", 4, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 5, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 6, false, "Recurring", "油烟机清洗", 5, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 6, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 12, false, "Recurring", "热水器除垢", 6, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 7, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 12, false, "Recurring", "烟雾报警器电池", 7, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 8, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 3, false, "Recurring", "冰箱除味剂", 8, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 9, "HomeMaintenance", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 1, false, "Recurring", "洗衣机槽清洁", 9, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 10, "Vehicle", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 6, false, "Recurring", "常规保养", 10, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 11, "Vehicle", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 12, false, "Recurring", "年检", 11, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 12, "Vehicle", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, "Month", 12, false, "Recurring", "交强险/商业险", 12, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 13, "Document", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, false, "OneOffExpiry", "身份证", 13, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 14, "Document", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, false, "OneOffExpiry", "护照", 14, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 15, "Document", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, false, "OneOffExpiry", "驾照", 15, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 16, "Document", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, false, "OneOffExpiry", "签证", 16, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 },
                    { 17, "Warranty", new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, false, "OneOffExpiry", "家电保修", 17, new DateTime(2026, 9, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdChatDraft_UserId_ExpiresAt",
                table: "HouseholdChatDraft",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdChatDraft_UserId_HouseholdId_ChatSessionId",
                table: "HouseholdChatDraft",
                columns: new[] { "UserId", "HouseholdId", "ChatSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdCompletionRecord_CompletedByMemberId",
                table: "HouseholdCompletionRecord",
                column: "CompletedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_CompletedOn",
                table: "HouseholdCompletionRecord",
                columns: new[] { "HouseholdItemId", "CompletedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_IdempotencyUserId_IdempotencyKey",
                table: "HouseholdCompletionRecord",
                columns: new[] { "HouseholdItemId", "IdempotencyUserId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdConsumable_HouseholdId",
                table: "HouseholdConsumable",
                column: "HouseholdId");

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
                name: "IX_HouseholdInvitation_HouseholdId_InviteeUserId",
                table: "HouseholdInvitation",
                columns: new[] { "HouseholdId", "InviteeUserId" },
                unique: true,
                filter: "[Status] = 'Pending' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdInvitation_InviteeUserId_Status_ExpiresAt",
                table: "HouseholdInvitation",
                columns: new[] { "InviteeUserId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdItem_AssigneeMemberId",
                table: "HouseholdItem",
                column: "AssigneeMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdItem_ConsumableId",
                table: "HouseholdItem",
                column: "ConsumableId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdItem_HouseholdId_Category",
                table: "HouseholdItem",
                columns: new[] { "HouseholdId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdItem_HouseholdId_IsPaused_NextDueDate",
                table: "HouseholdItem",
                columns: new[] { "HouseholdId", "IsPaused", "NextDueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdItemTemplate_SortOrder",
                table: "HouseholdItemTemplate",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMember_HouseholdId",
                table: "HouseholdMember",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMember_UserId",
                table: "HouseholdMember",
                column: "UserId",
                unique: true,
                filter: "[IsDeleted] = 0");

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
    }
}
