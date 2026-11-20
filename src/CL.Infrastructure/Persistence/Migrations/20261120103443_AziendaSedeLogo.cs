using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AziendaSedeLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cap",
                schema: "hr",
                table: "Aziende",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoChiave",
                schema: "hr",
                table: "Aziende",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provincia",
                schema: "hr",
                table: "Aziende",
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
                table: "Aziende");

            migrationBuilder.DropColumn(
                name: "LogoChiave",
                schema: "hr",
                table: "Aziende");

            migrationBuilder.DropColumn(
                name: "Provincia",
                schema: "hr",
                table: "Aziende");
        }
    }
}
