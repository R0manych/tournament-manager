using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zettel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchWalkoverSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Match_Fighter1NotEqualFighter2",
                table: "Matches");

            migrationBuilder.AlterColumn<Guid>(
                name: "Fighter2Id",
                table: "Matches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Match_Fighter1NotEqualFighter2",
                table: "Matches",
                sql: "\"Fighter2Id\" IS NULL OR \"Fighter1Id\" <> \"Fighter2Id\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Match_Fighter1NotEqualFighter2",
                table: "Matches");

            migrationBuilder.AlterColumn<Guid>(
                name: "Fighter2Id",
                table: "Matches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Match_Fighter1NotEqualFighter2",
                table: "Matches",
                sql: "\"Fighter1Id\" <> \"Fighter2Id\"");
        }
    }
}
