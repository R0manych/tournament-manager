using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zettel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameParticipantFighterToParticipantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentParticipants_Fighters_FighterId",
                table: "TournamentParticipants");

            migrationBuilder.DropIndex(
                name: "IX_TournamentParticipants_FighterId",
                table: "TournamentParticipants");

            migrationBuilder.RenameColumn(
                name: "FighterId",
                table: "TournamentParticipants",
                newName: "ParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ParticipantId",
                table: "TournamentParticipants",
                newName: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentParticipants_FighterId",
                table: "TournamentParticipants",
                column: "FighterId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentParticipants_Fighters_FighterId",
                table: "TournamentParticipants",
                column: "FighterId",
                principalTable: "Fighters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
