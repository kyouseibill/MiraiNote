using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Household",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Household", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdItemTemplate",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CycleValue = table.Column<int>(type: "int", nullable: true),
                    CycleUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
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
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SpecModel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CurrentStock = table.Column<int>(type: "int", nullable: false),
                    RestockThreshold = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    LowStockReminderSent = table.Column<bool>(type: "bit", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PurchaseLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
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
                name: "HouseholdMember",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
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
                name: "HouseholdItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModelSpec = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ItemType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CycleValue = table.Column<int>(type: "int", nullable: true),
                    CycleUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastDoneDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LeadDays = table.Column<int>(type: "int", nullable: false, defaultValue: 7),
                    AssigneeMemberId = table.Column<int>(type: "int", nullable: true),
                    ConsumableId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PurchaseLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
                    MileageCycleKm = table.Column<int>(type: "int", nullable: true),
                    AliasesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
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
                name: "HouseholdCompletionRecord",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdItemId = table.Column<int>(type: "int", nullable: false),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedByMemberId = table.Column<int>(type: "int", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: false),
                    CompletedByUsername = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PhotoRefs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PurchaseLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConsumableId = table.Column<int>(type: "int", nullable: true),
                    ConsumableQuantityDeducted = table.Column<int>(type: "int", nullable: false),
                    NeedsRestock = table.Column<bool>(type: "bit", nullable: false),
                    NewExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
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
                name: "IX_HouseholdCompletionRecord_CompletedByMemberId",
                table: "HouseholdCompletionRecord",
                column: "CompletedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdCompletionRecord_HouseholdItemId_CompletedOn",
                table: "HouseholdCompletionRecord",
                columns: new[] { "HouseholdItemId", "CompletedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdConsumable_HouseholdId",
                table: "HouseholdConsumable",
                column: "HouseholdId");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdCompletionRecord");

            migrationBuilder.DropTable(
                name: "HouseholdItemTemplate");

            migrationBuilder.DropTable(
                name: "HouseholdItem");

            migrationBuilder.DropTable(
                name: "HouseholdConsumable");

            migrationBuilder.DropTable(
                name: "HouseholdMember");

            migrationBuilder.DropTable(
                name: "Household");
        }
    }
}
