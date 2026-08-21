using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zettel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchVideoReplays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VideoReplays1",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoReplays2",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoReplays1",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "VideoReplays2",
                table: "Matches");
        }
    }
}
