using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FeaturedLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Featured",
                table: "ArmyLists",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Featured",
                table: "ArmyLists");
        }
    }
}
