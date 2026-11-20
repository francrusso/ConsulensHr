using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PresenzeMensili : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComunicazioniPresenze",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anno = table.Column<int>(type: "integer", nullable: false),
                    Mese = table.Column<int>(type: "integer", nullable: false),
                    Stato = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InviataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InviataDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ElaborataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ElaborataDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComunicazioniPresenze", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComunicazioniPresenze_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RighePresenze",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComunicazioneId = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    OreStraordinario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OreStraordinarioFestivo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OreNonRetribuite = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BuoniPasto = table.Column<int>(type: "integer", nullable: false),
                    Premi = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Rimborsi = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Anticipi = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OreFerie = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OreRol = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OreExFestivita = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OreMalattia = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OreAltreAssenze = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RighePresenze", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RighePresenze_ComunicazioniPresenze_ComunicazioneId",
                        column: x => x.ComunicazioneId,
                        principalSchema: "hr",
                        principalTable: "ComunicazioniPresenze",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RighePresenze_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComunicazioniPresenze_Anno_Mese_Stato",
                schema: "hr",
                table: "ComunicazioniPresenze",
                columns: new[] { "Anno", "Mese", "Stato" });

            migrationBuilder.CreateIndex(
                name: "IX_ComunicazioniPresenze_AziendaId_Anno_Mese",
                schema: "hr",
                table: "ComunicazioniPresenze",
                columns: new[] { "AziendaId", "Anno", "Mese" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RighePresenze_ComunicazioneId_DipendenteId",
                schema: "hr",
                table: "RighePresenze",
                columns: new[] { "ComunicazioneId", "DipendenteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RighePresenze_DipendenteId",
                schema: "hr",
                table: "RighePresenze",
                column: "DipendenteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RighePresenze",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "ComunicazioniPresenze",
                schema: "hr");
        }
    }
}
