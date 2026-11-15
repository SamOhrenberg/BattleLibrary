using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FreeformLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogueSystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_LoadoutPresets_UserId_GameSystemKey_DatasheetId",
                table: "LoadoutPresets");

            migrationBuilder.AlterColumn<int>(
                name: "Points",
                table: "LoadoutPresets",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ModelCount",
                table: "LoadoutPresets",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Enhancement",
                table: "LoadoutPresets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "LoadoutPresets",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Wargear",
                table: "LoadoutPresets",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Points",
                table: "ListEntries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ModelCount",
                table: "ListEntries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Wargear",
                table: "ListEntries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detachment",
                table: "ArmyLists",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ArmyLists",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            // Convert existing BSData-based rows into the freeform shape before the
            // old columns go: options become wargear text, points fold into one total.
            migrationBuilder.Sql("""
                UPDATE "ListEntries" SET
                    "Wargear" = (
                        SELECT left(string_agg(
                            CASE WHEN COALESCE((o->>'Quantity')::int, 1) > 1 THEN (o->>'Quantity') || 'x ' ELSE '' END || (o->>'Name'),
                            E'\n' ORDER BY ord), 2000)
                        FROM jsonb_array_elements(COALESCE("SelectedOptions", '[]'::jsonb)) WITH ORDINALITY AS t(o, ord)),
                    "Points" = NULLIF(
                        "Points" + "EnhancementPoints" + "DynamicPoints" + COALESCE((
                            SELECT sum((o->>'Points')::int * GREATEST(COALESCE((o->>'Quantity')::int, 1), 1))
                            FROM jsonb_array_elements(COALESCE("SelectedOptions", '[]'::jsonb)) AS o), 0),
                        0),
                    "DatasheetName" = CASE
                        WHEN "CustomName" IS NOT NULL THEN left("CustomName" || ' (' || "DatasheetName" || ')', 200)
                        ELSE "DatasheetName" END;

                UPDATE "LoadoutPresets" SET
                    "Wargear" = (
                        SELECT left(string_agg(
                            CASE WHEN COALESCE((o->>'Quantity')::int, 1) > 1 THEN (o->>'Quantity') || 'x ' ELSE '' END || (o->>'Name'),
                            E'\n' ORDER BY ord), 2000)
                        FROM jsonb_array_elements(COALESCE("SelectedOptions", '[]'::jsonb)) WITH ORDINALITY AS t(o, ord)),
                    "Points" = NULLIF(
                        "Points" + COALESCE((
                            SELECT sum((o->>'Points')::int * GREATEST(COALESCE((o->>'Quantity')::int, 1), 1))
                            FROM jsonb_array_elements(COALESCE("SelectedOptions", '[]'::jsonb)) AS o), 0),
                        0);

                UPDATE "ArmyLists" SET
                    "Detachment" = (
                        SELECT left(string_agg(d->>'Name', ', ' ORDER BY ord), 200)
                        FROM jsonb_array_elements(COALESCE("Detachments", '[]'::jsonb)) WITH ORDINALITY AS t(d, ord)),
                    "Notes" = NULLIF(concat_ws(E'\n',
                        'Battle size: ' || "BattleSize",
                        'Force disposition: ' || "ForceDisposition"), '');

                UPDATE "ArmyLists" SET "GameSystemKey" = CASE
                    WHEN "GameSystemKey" LIKE 'wh40k%' THEN 'wh40k'
                    WHEN "GameSystemKey" LIKE 'kill-team%' THEN 'kill-team'
                    ELSE 'other' END;
                UPDATE "LoadoutPresets" SET "GameSystemKey" = CASE
                    WHEN "GameSystemKey" LIKE 'wh40k%' THEN 'wh40k'
                    WHEN "GameSystemKey" LIKE 'kill-team%' THEN 'kill-team'
                    ELSE 'other' END;
                UPDATE "Games" SET "GameSystemKey" = CASE
                    WHEN "GameSystemKey" = '' THEN ''
                    WHEN "GameSystemKey" LIKE 'wh40k%' THEN 'wh40k'
                    WHEN "GameSystemKey" LIKE 'kill-team%' THEN 'kill-team'
                    ELSE 'other' END;
                UPDATE "GameSessions" SET "GameSystemKey" = CASE
                    WHEN "GameSystemKey" = '' THEN ''
                    WHEN "GameSystemKey" LIKE 'wh40k%' THEN 'wh40k'
                    WHEN "GameSystemKey" LIKE 'kill-team%' THEN 'kill-team'
                    ELSE 'other' END;

                UPDATE "GameParticipants" SET "Units" = (
                    SELECT COALESCE(jsonb_agg(u - 'DatasheetId' - 'Keywords' ORDER BY ord), '[]'::jsonb)
                    FROM jsonb_array_elements("Units") WITH ORDINALITY AS t(u, ord))
                WHERE jsonb_typeof("Units") = 'array';
                """);

            migrationBuilder.DropColumn(
                name: "DatasheetId",
                table: "LoadoutPresets");

            migrationBuilder.DropColumn(
                name: "SelectedOptions",
                table: "LoadoutPresets");

            migrationBuilder.DropColumn(
                name: "FactionId",
                table: "ListForces");

            migrationBuilder.DropColumn(
                name: "CustomName",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "DatasheetId",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "DynamicPoints",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "EnhancementId",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "EnhancementPoints",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "ForcedWarlord",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "Keywords",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "SelectedOptions",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "BattleSize",
                table: "ArmyLists");

            migrationBuilder.DropColumn(
                name: "Detachments",
                table: "ArmyLists");

            migrationBuilder.DropColumn(
                name: "FactionId",
                table: "ArmyLists");

            migrationBuilder.DropColumn(
                name: "ForceDisposition",
                table: "ArmyLists");

            migrationBuilder.RenameColumn(
                name: "DatasheetName",
                table: "LoadoutPresets",
                newName: "UnitName");

            migrationBuilder.RenameColumn(
                name: "FactionName",
                table: "ListForces",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "EnhancementName",
                table: "ListEntries",
                newName: "Enhancement");

            migrationBuilder.RenameColumn(
                name: "DatasheetName",
                table: "ListEntries",
                newName: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_LoadoutPresets_UserId_GameSystemKey",
                table: "LoadoutPresets",
                columns: new[] { "UserId", "GameSystemKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoadoutPresets_UserId_GameSystemKey",
                table: "LoadoutPresets");

            migrationBuilder.DropColumn(
                name: "Enhancement",
                table: "LoadoutPresets");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "LoadoutPresets");

            migrationBuilder.DropColumn(
                name: "Wargear",
                table: "LoadoutPresets");

            migrationBuilder.DropColumn(
                name: "Wargear",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "Detachment",
                table: "ArmyLists");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ArmyLists");

            migrationBuilder.RenameColumn(
                name: "UnitName",
                table: "LoadoutPresets",
                newName: "DatasheetName");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ListForces",
                newName: "FactionName");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ListEntries",
                newName: "DatasheetName");

            migrationBuilder.RenameColumn(
                name: "Enhancement",
                table: "ListEntries",
                newName: "EnhancementName");

            migrationBuilder.AlterColumn<int>(
                name: "Points",
                table: "LoadoutPresets",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ModelCount",
                table: "LoadoutPresets",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DatasheetId",
                table: "LoadoutPresets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SelectedOptions",
                table: "LoadoutPresets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FactionId",
                table: "ListForces",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "Points",
                table: "ListEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ModelCount",
                table: "ListEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomName",
                table: "ListEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DatasheetId",
                table: "ListEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DynamicPoints",
                table: "ListEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EnhancementId",
                table: "ListEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnhancementPoints",
                table: "ListEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ForcedWarlord",
                table: "ListEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "Keywords",
                table: "ListEntries",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "ListEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SelectedOptions",
                table: "ListEntries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BattleSize",
                table: "ArmyLists",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detachments",
                table: "ArmyLists",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FactionId",
                table: "ArmyLists",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ForceDisposition",
                table: "ArmyLists",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalogueSystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueSystemSettings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoadoutPresets_UserId_GameSystemKey_DatasheetId",
                table: "LoadoutPresets",
                columns: new[] { "UserId", "GameSystemKey", "DatasheetId" });
        }
    }
}
