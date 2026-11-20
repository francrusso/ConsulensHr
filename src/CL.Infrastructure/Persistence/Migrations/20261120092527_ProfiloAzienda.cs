using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfiloAzienda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfiliAzienda",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Passo = table.Column<int>(type: "integer", nullable: false),
                    FormaGiuridica = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    NumeroRea = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CodiceAteco = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DataInizioAttivita = table.Column<DateOnly>(type: "date", nullable: true),
                    Pec = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Telefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SedeLegaleVia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SedeLegaleCap = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    SedeLegaleComune = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    SedeLegaleProvincia = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    SedeOperativaVia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SedeOperativaCap = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    SedeOperativaComune = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    SedeOperativaProvincia = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    LegaleNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    LegaleCognome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    LegaleCodiceFiscale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LegaleEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LegaleTelefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Ccnl = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    MatricolaInps = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    InailCodiceDitta = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    InailPat = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CassaEdile = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EnteBilaterale = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    FondoPensione = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    GiorniLavorativiSettimana = table.Column<int>(type: "integer", nullable: true),
                    OreSettimanali = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    IbanAddebito = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    GiornoPagamento = table.Column<int>(type: "integer", nullable: true),
                    ReferenteNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ReferenteEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReferenteTelefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NoteStudio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PrivacyAccettataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletatoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletatoDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfiliAzienda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfiliAzienda_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfiliAzienda_AziendaId",
                schema: "hr",
                table: "ProfiliAzienda",
                column: "AziendaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfiliAzienda",
                schema: "hr");
        }
    }
}
