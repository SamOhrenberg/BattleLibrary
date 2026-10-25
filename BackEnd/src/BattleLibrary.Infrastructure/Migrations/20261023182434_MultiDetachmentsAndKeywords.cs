using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultiDetachmentsAndKeywords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetachmentId",
                table: "ArmyLists");

            migrationBuilder.DropColumn(
                name: "DetachmentName",
                table: "ArmyLists");

            migrationBuilder.AddColumn<string>(
                name: "KeywordsCsv",
                table: "CachedDatasheets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Detachments",
                table: "ArmyLists",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeywordsCsv",
                table: "CachedDatasheets");

            migrationBuilder.DropColumn(
                name: "Detachments",
                table: "ArmyLists");

            migrationBuilder.AddColumn<string>(
                name: "DetachmentId",
                table: "ArmyLists",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetachmentName",
                table: "ArmyLists",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
