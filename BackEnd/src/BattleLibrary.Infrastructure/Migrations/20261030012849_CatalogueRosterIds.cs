using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueRosterIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BsDataId",
                table: "CachedGameSystems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BsDataName",
                table: "CachedGameSystems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PointsCostTypeId",
                table: "CachedGameSystems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "CachedGameSystems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "CachedFactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BsDataId",
                table: "CachedGameSystems");

            migrationBuilder.DropColumn(
                name: "BsDataName",
                table: "CachedGameSystems");

            migrationBuilder.DropColumn(
                name: "PointsCostTypeId",
                table: "CachedGameSystems");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "CachedGameSystems");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "CachedFactions");
        }
    }
}
