using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueInMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            // Keep each system's kill-switch state; the catalogue content itself is dropped.
            migrationBuilder.Sql("""
                INSERT INTO "CatalogueSystemSettings" ("Key", "Enabled")
                SELECT "Key", "Enabled" FROM "CachedGameSystems";
                """);

            migrationBuilder.DropTable(
                name: "CachedDatasheets");

            migrationBuilder.DropTable(
                name: "CachedFactions");

            migrationBuilder.DropTable(
                name: "CachedGameSystems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogueSystemSettings");

            migrationBuilder.CreateTable(
                name: "CachedGameSystems",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BsDataId = table.Column<string>(type: "text", nullable: false),
                    BsDataName = table.Column<string>(type: "text", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DispositionsJson = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ForceEntryId = table.Column<string>(type: "text", nullable: false),
                    ForceEntryName = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PointsCostTypeId = table.Column<string>(type: "text", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Repo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedGameSystems", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CachedFactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameSystemKey = table.Column<string>(type: "character varying(100)", nullable: false),
                    BsDataId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DatasheetCount = table.Column<int>(type: "integer", nullable: false),
                    DetachmentsJson = table.Column<string>(type: "text", nullable: false),
                    EquipmentJson = table.Column<string>(type: "jsonb", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedFactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CachedFactions_CachedGameSystems_GameSystemKey",
                        column: x => x.GameSystemKey,
                        principalTable: "CachedGameSystems",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CachedDatasheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailabilityJson = table.Column<string>(type: "jsonb", nullable: false),
                    BsDataId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeywordsCsv = table.Column<string>(type: "text", nullable: false),
                    ModelJson = table.Column<string>(type: "jsonb", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: true),
                    SortIndex = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedDatasheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CachedDatasheets_CachedFactions_FactionId",
                        column: x => x.FactionId,
                        principalTable: "CachedFactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CachedDatasheets_BsDataId",
                table: "CachedDatasheets",
                column: "BsDataId");

            migrationBuilder.CreateIndex(
                name: "IX_CachedDatasheets_FactionId",
                table: "CachedDatasheets",
                column: "FactionId");

            migrationBuilder.CreateIndex(
                name: "IX_CachedFactions_BsDataId",
                table: "CachedFactions",
                column: "BsDataId");

            migrationBuilder.CreateIndex(
                name: "IX_CachedFactions_GameSystemKey",
                table: "CachedFactions",
                column: "GameSystemKey");
        }
    }
}
