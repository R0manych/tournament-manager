using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zettel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchFighterTeamCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Match_FighterXorTeam",
                table: "Matches",
                sql: "(\"Fighter1Id\" IS NOT NULL AND \"Team1Id\" IS NULL AND \"Team2Id\" IS NULL) OR (\"Team1Id\" IS NOT NULL AND \"Fighter1Id\" IS NULL AND \"Fighter2Id\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Match_Team1NotEqualTeam2",
                table: "Matches",
                sql: "\"Team2Id\" IS NULL OR \"Team1Id\" <> \"Team2Id\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Match_FighterXorTeam",
                table: "Matches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Match_Team1NotEqualTeam2",
                table: "Matches");
        }
    }
}
