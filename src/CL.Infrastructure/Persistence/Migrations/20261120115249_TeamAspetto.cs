using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamAspetto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Colore",
                schema: "hr",
                table: "Team",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#A47FE6");

            migrationBuilder.AddColumn<string>(
                name: "Emoji",
                schema: "hr",
                table: "Team",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Colore",
                schema: "hr",
                table: "Team");

            migrationBuilder.DropColumn(
                name: "Emoji",
                schema: "hr",
                table: "Team");
        }
    }
}
