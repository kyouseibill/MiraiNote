using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiraiNote.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWeatherPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WeatherPlace",
                table: "User",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeatherPlace",
                table: "User");
        }
    }
}
