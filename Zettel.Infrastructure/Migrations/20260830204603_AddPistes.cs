using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zettel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPistes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PisteId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PisteId",
                table: "Encounters",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Pistes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pistes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pistes_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_PisteId_Status",
                table: "Matches",
                columns: new[] { "PisteId", "Status" },
                filter: "\"PisteId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_PisteId_Status",
                table: "Encounters",
                columns: new[] { "PisteId", "Status" },
                filter: "\"PisteId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pistes_TournamentId_Name",
                table: "Pistes",
                columns: new[] { "TournamentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pistes_TournamentId_OrderIndex",
                table: "Pistes",
                columns: new[] { "TournamentId", "OrderIndex" });

            migrationBuilder.AddForeignKey(
                name: "FK_Encounters_Pistes_PisteId",
                table: "Encounters",
                column: "PisteId",
                principalTable: "Pistes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_Pistes_PisteId",
                table: "Matches",
                column: "PisteId",
                principalTable: "Pistes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Encounters_Pistes_PisteId",
                table: "Encounters");

            migrationBuilder.DropForeignKey(
                name: "FK_Matches_Pistes_PisteId",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "Pistes");

            migrationBuilder.DropIndex(
                name: "IX_Matches_PisteId_Status",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Encounters_PisteId_Status",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "PisteId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PisteId",
                table: "Encounters");
        }
    }
}
