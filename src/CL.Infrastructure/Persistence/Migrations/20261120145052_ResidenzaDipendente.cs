using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResidenzaDipendente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cap",
                schema: "hr",
                table: "Dipendenti",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Comune",
                schema: "hr",
                table: "Dipendenti",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provincia",
                schema: "hr",
                table: "Dipendenti",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cap",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "Comune",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "Provincia",
                schema: "hr",
                table: "Dipendenti");
        }
    }
}
