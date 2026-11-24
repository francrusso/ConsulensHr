using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CicliGiornalieriTurni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GiorniSettimana",
                schema: "hr",
                table: "TurniSalvati",
                type: "integer",
                nullable: false,
                defaultValue: 127);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                schema: "hr",
                table: "TurniSalvati",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                schema: "hr",
                table: "TurniLavoro",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Lavoro");

            migrationBuilder.CreateTable(
                name: "FasiTurniSalvati",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordine = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Inizio = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Fine = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    TurnoSalvatoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FasiTurniSalvati", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FasiTurniSalvati_TurniSalvati_TurnoSalvatoId",
                        column: x => x.TurnoSalvatoId,
                        principalSchema: "hr",
                        principalTable: "TurniSalvati",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FasiTurniSalvati_TurnoSalvatoId_Ordine",
                schema: "hr",
                table: "FasiTurniSalvati",
                columns: new[] { "TurnoSalvatoId", "Ordine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FasiTurniSalvati",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "GiorniSettimana",
                schema: "hr",
                table: "TurniSalvati");

            migrationBuilder.DropColumn(
                name: "TeamId",
                schema: "hr",
                table: "TurniSalvati");

            migrationBuilder.DropColumn(
                name: "Tipo",
                schema: "hr",
                table: "TurniLavoro");
        }
    }
}
