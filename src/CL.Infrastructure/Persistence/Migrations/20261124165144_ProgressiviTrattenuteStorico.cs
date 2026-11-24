using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProgressiviTrattenuteStorico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ProgressiviAl",
                schema: "hr",
                table: "Dipendenti",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Ratei13",
                schema: "hr",
                table: "Dipendenti",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Ratei14",
                schema: "hr",
                table: "Dipendenti",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TfrAzienda",
                schema: "hr",
                table: "Dipendenti",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TfrFondi",
                schema: "hr",
                table: "Dipendenti",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentiStorici",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Anno = table.Column<int>(type: "integer", nullable: false),
                    Descrizione = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    NomeFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TipoFile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChiaveArchivio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DimensioneByte = table.Column<long>(type: "bigint", nullable: false),
                    CaricatoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CaricatoDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentiStorici", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentiStorici_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrattenuteBenefit",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Descrizione = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ImportoMensile = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Residuo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Dal = table.Column<DateOnly>(type: "date", nullable: false),
                    Al = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrattenuteBenefit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrattenuteBenefit_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrattenuteBenefit_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentiStorici_AziendaId_Anno",
                schema: "hr",
                table: "DocumentiStorici",
                columns: new[] { "AziendaId", "Anno" });

            migrationBuilder.CreateIndex(
                name: "IX_TrattenuteBenefit_AziendaId",
                schema: "hr",
                table: "TrattenuteBenefit",
                column: "AziendaId");

            migrationBuilder.CreateIndex(
                name: "IX_TrattenuteBenefit_DipendenteId",
                schema: "hr",
                table: "TrattenuteBenefit",
                column: "DipendenteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentiStorici",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "TrattenuteBenefit",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "ProgressiviAl",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "Ratei13",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "Ratei14",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "TfrAzienda",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "TfrFondi",
                schema: "hr",
                table: "Dipendenti");
        }
    }
}
