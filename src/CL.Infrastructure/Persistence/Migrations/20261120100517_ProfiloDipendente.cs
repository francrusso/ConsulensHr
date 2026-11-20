using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfiloDipendente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfiliDipendente",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Passo = table.Column<int>(type: "integer", nullable: false),
                    ComuneNascita = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ProvinciaNascita = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Cittadinanza = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Telefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    EmailPersonale = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResidenzaVia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ResidenzaCap = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    ResidenzaComune = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ResidenzaProvincia = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    DomicilioVia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DomicilioCap = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    DomicilioComune = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DomicilioProvincia = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    IntestatarioConto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Tfr = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FondoPensione = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    RichiedeDetrazioni = table.Column<bool>(type: "boolean", nullable: true),
                    TitoloStudio = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TipoDocumento = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ScadenzaDocumento = table.Column<DateOnly>(type: "date", nullable: true),
                    ScadenzaPermessoSoggiorno = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletatoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AggiornatoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AggiornatoDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfiliDipendente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfiliDipendente_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamiliariACarico",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfiloId = table.Column<Guid>(type: "uuid", nullable: false),
                    Relazione = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cognome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CodiceFiscale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DataNascita = table.Column<DateOnly>(type: "date", nullable: false),
                    PercentualeCarico = table.Column<int>(type: "integer", nullable: false),
                    Disabile = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamiliariACarico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamiliariACarico_ProfiliDipendente_ProfiloId",
                        column: x => x.ProfiloId,
                        principalSchema: "hr",
                        principalTable: "ProfiliDipendente",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamiliariACarico_ProfiloId",
                schema: "hr",
                table: "FamiliariACarico",
                column: "ProfiloId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfiliDipendente_DipendenteId",
                schema: "hr",
                table: "ProfiliDipendente",
                column: "DipendenteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamiliariACarico",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "ProfiliDipendente",
                schema: "hr");
        }
    }
}
