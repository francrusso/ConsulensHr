using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModalitaESede : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sede",
                schema: "hr",
                table: "Dipendenti",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GiorniInUfficio",
                schema: "hr",
                table: "Contratti",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Modalita",
                schema: "hr",
                table: "Contratti",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "InPresenza"); // contratti già registrati: in presenza
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sede",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "GiorniInUfficio",
                schema: "hr",
                table: "Contratti");

            migrationBuilder.DropColumn(
                name: "Modalita",
                schema: "hr",
                table: "Contratti");
        }
    }
}
