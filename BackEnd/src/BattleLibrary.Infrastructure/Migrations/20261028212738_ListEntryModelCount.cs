using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListEntryModelCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModelCount",
                table: "ListEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelCount",
                table: "ListEntries");
        }
    }
}
