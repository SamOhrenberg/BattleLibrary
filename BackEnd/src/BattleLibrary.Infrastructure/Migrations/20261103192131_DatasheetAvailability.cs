using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DatasheetAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvailabilityJson",
                table: "CachedDatasheets",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailabilityJson",
                table: "CachedDatasheets");
        }
    }
}
