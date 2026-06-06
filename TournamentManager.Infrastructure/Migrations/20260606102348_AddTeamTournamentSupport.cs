using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamTournamentSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultTeamBoutDurationSeconds",
                table: "Tournaments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultTeamTargetScore",
                table: "Tournaments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParticipantKind",
                table: "Tournaments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Fighter");

            migrationBuilder.AddColumn<int>(
                name: "BoutNumber",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetCumulativeScore",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Club = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Participant1Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Participant2Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TargetTotalScore = table.Column<int>(type: "integer", nullable: false),
                    BoutDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    WinnerParticipantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PriorityParticipantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Encounters", x => x.Id);
                    table.CheckConstraint("CK_Encounter_Participant1NotEqualParticipant2", "\"Participant1Id\" <> \"Participant2Id\"");
                    table.ForeignKey(
                        name: "FK_Encounters_Teams_Participant1Id",
                        column: x => x.Participant1Id,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Encounters_Teams_Participant2Id",
                        column: x => x.Participant2Id,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Encounters_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    FighterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => new { x.TeamId, x.FighterId });
                    table.ForeignKey(
                        name: "FK_TeamMembers_Fighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_EncounterId_BoutNumber",
                table: "Matches",
                columns: new[] { "EncounterId", "BoutNumber" },
                unique: true,
                filter: "\"EncounterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_Participant1Id",
                table: "Encounters",
                column: "Participant1Id");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_Participant2Id",
                table: "Encounters",
                column: "Participant2Id");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_TournamentId_Status",
                table: "Encounters",
                columns: new[] { "TournamentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_FighterId",
                table: "TeamMembers",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_Position",
                table: "TeamMembers",
                columns: new[] { "TeamId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TournamentId_Name",
                table: "Teams",
                columns: new[] { "TournamentId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_Encounters_EncounterId",
                table: "Matches",
                column: "EncounterId",
                principalTable: "Encounters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Matches_Encounters_EncounterId",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "Encounters");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Matches_EncounterId_BoutNumber",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "DefaultTeamBoutDurationSeconds",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "DefaultTeamTargetScore",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "ParticipantKind",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "BoutNumber",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "EncounterId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "TargetCumulativeScore",
                table: "Matches");
        }
    }
}
