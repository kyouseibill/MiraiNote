using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWelcomePhrase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WelcomePhrase",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Special = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelcomePhrase", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WelcomePhrase_Kind",
                table: "WelcomePhrase",
                column: "Kind");

            // 运行账户 appuser 不是建表角色。漏了表和 identity 序列的授权时，
            // SELECT/INSERT/UPDATE/DELETE 都会失败。角色还不存在时跳过，避免 CI 空库迁移失败。
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'appuser') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE "WelcomePhrase" TO appuser;
                        GRANT USAGE, SELECT ON SEQUENCE "WelcomePhrase_Id_seq" TO appuser;
                    END IF;
                END
                $$;
                """);

            WelcomePhraseMigrationSeed.Insert(migrationBuilder);

            migrationBuilder.Sql(
                """
                SELECT setval(pg_get_serial_sequence('"WelcomePhrase"', 'Id'), (SELECT MAX("Id") FROM "WelcomePhrase"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WelcomePhrase");
        }
    }
}
