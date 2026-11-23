using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnnullamentoRichiesto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnnullamentoRichiestoIl",
                schema: "hr",
                table: "RichiesteAssenza",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoAnnullamento",
                schema: "hr",
                table: "RichiesteAssenza",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnullamentoRichiestoIl",
                schema: "hr",
                table: "RichiesteAssenza");

            migrationBuilder.DropColumn(
                name: "MotivoAnnullamento",
                schema: "hr",
                table: "RichiesteAssenza");
        }
    }
}
