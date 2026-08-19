using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zettel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchPlacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchPlacements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhaseId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RoundId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPlacements_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchPlacements_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlacements_MatchId",
                table: "MatchPlacements",
                column: "MatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlacements_TournamentId_PhaseId_RoundId_SlotIndex",
                table: "MatchPlacements",
                columns: new[] { "TournamentId", "PhaseId", "RoundId", "SlotIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchPlacements");
        }
    }
}
