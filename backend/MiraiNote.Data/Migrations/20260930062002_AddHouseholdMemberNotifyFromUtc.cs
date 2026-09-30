using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdMemberNotifyFromUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NotifyFromUtc",
                table: "HouseholdMember",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // 老成员的加入起点用当时的 CreatedAt，不能用这次上线的时刻，否则会丢掉他们仍应收到的补发。
            migrationBuilder.Sql("""
                UPDATE [HouseholdMember] SET [NotifyFromUtc] = [CreatedAt];
                DECLARE @constraint nvarchar(200);
                SELECT @constraint = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                WHERE dc.parent_object_id = OBJECT_ID(N'[HouseholdMember]')
                  AND c.name = N'NotifyFromUtc';
                IF @constraint IS NOT NULL
                    EXEC(N'ALTER TABLE [HouseholdMember] DROP CONSTRAINT [' + @constraint + N']');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyFromUtc",
                table: "HouseholdMember");
        }
    }
}
