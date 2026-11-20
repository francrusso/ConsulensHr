using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContrattoDocumento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentoCaricatoIl",
                schema: "hr",
                table: "Contratti",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentoChiave",
                schema: "hr",
                table: "Contratti",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentoNome",
                schema: "hr",
                table: "Contratti",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentoCaricatoIl",
                schema: "hr",
                table: "Contratti");

            migrationBuilder.DropColumn(
                name: "DocumentoChiave",
                schema: "hr",
                table: "Contratti");

            migrationBuilder.DropColumn(
                name: "DocumentoNome",
                schema: "hr",
                table: "Contratti");
        }
    }
}
