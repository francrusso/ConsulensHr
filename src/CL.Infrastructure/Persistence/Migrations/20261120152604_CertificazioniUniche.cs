using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CertificazioniUniche : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CertificazioniUniche",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnnoImposta = table.Column<int>(type: "integer", nullable: false),
                    NomeFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ChiaveArchivio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DimensioneByte = table.Column<long>(type: "bigint", nullable: false),
                    CaricataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CaricataDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    VisualizzataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificazioniUniche", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificazioniUniche_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CertificazioniUniche_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificazioniUniche_AziendaId",
                schema: "hr",
                table: "CertificazioniUniche",
                column: "AziendaId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificazioniUniche_DipendenteId_AnnoImposta",
                schema: "hr",
                table: "CertificazioniUniche",
                columns: new[] { "DipendenteId", "AnnoImposta" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificazioniUniche",
                schema: "hr");
        }
    }
}
