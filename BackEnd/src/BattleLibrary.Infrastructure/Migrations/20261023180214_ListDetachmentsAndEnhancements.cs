using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListDetachmentsAndEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnhancementId",
                table: "ListEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnhancementName",
                table: "ListEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnhancementPoints",
                table: "ListEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsWarlord",
                table: "ListEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "Keywords",
                table: "ListEntries",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "DetachmentsJson",
                table: "CachedFactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BattleSize",
                table: "ArmyLists",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnhancementId",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "EnhancementName",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "EnhancementPoints",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "IsWarlord",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "Keywords",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "DetachmentsJson",
                table: "CachedFactions");

            migrationBuilder.DropColumn(
                name: "BattleSize",
                table: "ArmyLists");

            migrationBuilder.DropColumn(
                name: "DetachmentId",
                table: "ArmyLists");

            migrationBuilder.DropColumn(
                name: "DetachmentName",
                table: "ArmyLists");
        }
    }
}
