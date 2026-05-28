using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRoundsPerMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultRoundsPerMatch",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "TotalRounds",
                table: "Matches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultRoundsPerMatch",
                table: "Tournaments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalRounds",
                table: "Matches",
                type: "integer",
                nullable: true);
        }
    }
}
