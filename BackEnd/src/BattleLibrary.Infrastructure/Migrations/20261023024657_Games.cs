using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BattleLibrary.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Games : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    GameSystemKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlayerFactionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PlayerListId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpponentUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpponentName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OpponentFactionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    PlayerScore = table.Column<int>(type: "integer", nullable: true),
                    OpponentScore = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Games_ArmyLists_PlayerListId",
                        column: x => x.PlayerListId,
                        principalTable: "ArmyLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Games_Users_OpponentUserId",
                        column: x => x.OpponentUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Games_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_OpponentUserId",
                table: "Games",
                column: "OpponentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_PlayerListId",
                table: "Games",
                column: "PlayerListId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_UserId_PlayedOn",
                table: "Games",
                columns: new[] { "UserId", "PlayedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
