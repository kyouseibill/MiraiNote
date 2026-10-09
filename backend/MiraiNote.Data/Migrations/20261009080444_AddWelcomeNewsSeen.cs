using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWelcomeNewsSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WelcomeNewsSeen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ShownAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelcomeNewsSeen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WelcomeNewsSeen_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeNewsSeen_UserId_ShownAt",
                table: "WelcomeNewsSeen",
                columns: new[] { "UserId", "ShownAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WelcomeNewsSeen_UserId_Url",
                table: "WelcomeNewsSeen",
                columns: new[] { "UserId", "Url" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            // 运行账户 appuser 不是建表角色。漏了表和 identity 序列的授权时，
            // SELECT/INSERT/UPDATE/DELETE 都会失败，已读记录写不进去。
            // 仓库里此前没有 GRANT appuser。角色还不存在时跳过，避免 CI 空库（只有 postgres）迁移失败。
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'appuser') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE "WelcomeNewsSeen" TO appuser;
                        GRANT USAGE, SELECT ON SEQUENCE "WelcomeNewsSeen_Id_seq" TO appuser;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WelcomeNewsSeen");
        }
    }
}
