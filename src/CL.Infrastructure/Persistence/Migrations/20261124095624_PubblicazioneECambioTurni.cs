using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PubblicazioneECambioTurni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PubblicatoIl",
                schema: "hr",
                table: "TurniLavoro",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamVincolatoId",
                schema: "hr",
                table: "TurniLavoro",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Versione",
                schema: "hr",
                table: "TurniLavoro",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RichiesteCambioTurno",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnoCollegaId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollegaId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccordoCollega = table.Column<bool>(type: "boolean", nullable: false),
                    VersioneTurno = table.Column<int>(type: "integer", nullable: false),
                    VersioneCollega = table.Column<int>(type: "integer", nullable: true),
                    InizioOriginale = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FineOriginale = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NuovoInizio = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NuovaFine = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Stato = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RichiestoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecisoDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MotivoRifiuto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RichiesteCambioTurno", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RichiesteCambioTurno_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteCambioTurno_DipendenteId_Stato",
                schema: "hr",
                table: "RichiesteCambioTurno",
                columns: new[] { "DipendenteId", "Stato" });

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteCambioTurno_TurnoId",
                schema: "hr",
                table: "RichiesteCambioTurno",
                column: "TurnoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RichiesteCambioTurno",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "PubblicatoIl",
                schema: "hr",
                table: "TurniLavoro");

            migrationBuilder.DropColumn(
                name: "TeamVincolatoId",
                schema: "hr",
                table: "TurniLavoro");

            migrationBuilder.DropColumn(
                name: "Versione",
                schema: "hr",
                table: "TurniLavoro");
        }
    }
}
