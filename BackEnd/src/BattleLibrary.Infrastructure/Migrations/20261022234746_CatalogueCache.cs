using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CachedGameSystems",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Repo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DatasheetCount = table.Column<int>(type: "integer", nullable: false)
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
                    BsDataId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: true),
                    ModelJson = table.Column<string>(type: "jsonb", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CachedDatasheets");

            migrationBuilder.DropTable(
                name: "CachedFactions");

            migrationBuilder.DropTable(
                name: "CachedGameSystems");
        }
    }
}
