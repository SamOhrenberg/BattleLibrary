using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Lists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArmyLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    GameSystemKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FactionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PointsLimit = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArmyLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArmyLists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArmyListId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasheetId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DatasheetName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    SelectedOptions = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListEntries_ArmyLists_ArmyListId",
                        column: x => x.ArmyListId,
                        principalTable: "ArmyLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArmyLists_UserId",
                table: "ArmyLists",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_ArmyListId",
                table: "ListEntries",
                column: "ArmyListId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListEntries");

            migrationBuilder.DropTable(
                name: "ArmyLists");
        }
    }
}
