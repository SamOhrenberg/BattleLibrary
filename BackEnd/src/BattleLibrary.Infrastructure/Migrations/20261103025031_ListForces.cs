using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListForces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ListForceId",
                table: "ListEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ListForces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArmyListId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FactionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListForces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListForces_ArmyLists_ArmyListId",
                        column: x => x.ArmyListId,
                        principalTable: "ArmyLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_ListForceId",
                table: "ListEntries",
                column: "ListForceId");

            migrationBuilder.CreateIndex(
                name: "IX_ListForces_ArmyListId",
                table: "ListForces",
                column: "ArmyListId");

            // Backfill: give every existing list a primary force from its own
            // faction, then point each existing entry at its list's force.
            migrationBuilder.Sql(
                """
                INSERT INTO "ListForces" ("Id", "ArmyListId", "FactionId", "FactionName", "SortOrder", "CreatedAt")
                SELECT gen_random_uuid(), l."Id", l."FactionId", l."FactionName", 0, now()
                FROM "ArmyLists" l;
                """);
            migrationBuilder.Sql(
                """
                UPDATE "ListEntries" e
                SET "ListForceId" = f."Id"
                FROM "ListForces" f
                WHERE f."ArmyListId" = e."ArmyListId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListForces");

            migrationBuilder.DropIndex(
                name: "IX_ListEntries_ListForceId",
                table: "ListEntries");

            migrationBuilder.DropColumn(
                name: "ListForceId",
                table: "ListEntries");
        }
    }
}
