using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnitaLocaliTitolariSubentro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodiciAutorizzazione",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsulentePrecedente",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContattiPrecedente",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContrattiIntegrativi",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Csc",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DataSubentro",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LegaleUnicoTitolare",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Subentro",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "UltimoMeseElaborato",
                schema: "hr",
                table: "ProfiliAzienda",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitaLocaleId",
                schema: "hr",
                table: "Dipendenti",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoAvvio",
                schema: "hr",
                table: "Aziende",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NuovaAttivita");

            migrationBuilder.CreateTable(
                name: "TitolariEffettivi",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cognome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CodiceFiscale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Quota = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    ProfiloAziendaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitolariEffettivi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TitolariEffettivi_ProfiliAzienda_ProfiloAziendaId",
                        column: x => x.ProfiloAziendaId,
                        principalSchema: "hr",
                        principalTable: "ProfiliAzienda",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UnitaLocali",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Via = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Cap = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Comune = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Provincia = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ReferenteNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ReferenteEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReferenteTelefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ProfiloAziendaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitaLocali", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitaLocali_ProfiliAzienda_ProfiloAziendaId",
                        column: x => x.ProfiloAziendaId,
                        principalSchema: "hr",
                        principalTable: "ProfiliAzienda",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VociInail",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Voce = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tasso = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    ProfiloAziendaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VociInail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VociInail_ProfiliAzienda_ProfiloAziendaId",
                        column: x => x.ProfiloAziendaId,
                        principalSchema: "hr",
                        principalTable: "ProfiliAzienda",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TitolariEffettivi_ProfiloAziendaId",
                schema: "hr",
                table: "TitolariEffettivi",
                column: "ProfiloAziendaId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitaLocali_ProfiloAziendaId",
                schema: "hr",
                table: "UnitaLocali",
                column: "ProfiloAziendaId");

            migrationBuilder.CreateIndex(
                name: "IX_VociInail_ProfiloAziendaId",
                schema: "hr",
                table: "VociInail",
                column: "ProfiloAziendaId");

            // La vecchia sede operativa diventa la prima unità locale; chi ci lavorava passa all'unità.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    INSERT INTO hr."UnitaLocali" ("Id", "Nome", "Via", "Cap", "Comune", "Provincia", "ProfiloAziendaId")
                    SELECT gen_random_uuid(), 'Sede operativa', p."SedeOperativaVia", p."SedeOperativaCap", p."SedeOperativaComune", p."SedeOperativaProvincia", p."Id"
                    FROM hr."ProfiliAzienda" p
                    WHERE p."SedeOperativaVia" IS NOT NULL AND p."SedeOperativaCap" IS NOT NULL
                      AND p."SedeOperativaComune" IS NOT NULL AND p."SedeOperativaProvincia" IS NOT NULL;

                    UPDATE hr."Dipendenti" d
                    SET "Sede" = 'UnitaLocale', "UnitaLocaleId" = u."Id"
                    FROM hr."ProfiliAzienda" p
                    JOIN hr."UnitaLocali" u ON u."ProfiloAziendaId" = p."Id"
                    WHERE d."Sede" = 'SedeOperativa' AND p."AziendaId" = d."AziendaId";

                    UPDATE hr."ProfiliAzienda"
                    SET "SedeOperativaVia" = NULL, "SedeOperativaCap" = NULL, "SedeOperativaComune" = NULL, "SedeOperativaProvincia" = NULL
                    WHERE "Id" IN (SELECT "ProfiloAziendaId" FROM hr."UnitaLocali");
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TitolariEffettivi",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "UnitaLocali",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "VociInail",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "CodiciAutorizzazione",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "ConsulentePrecedente",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "ContattiPrecedente",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "ContrattiIntegrativi",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "Csc",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "DataSubentro",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "LegaleUnicoTitolare",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "Subentro",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "UltimoMeseElaborato",
                schema: "hr",
                table: "ProfiliAzienda");

            migrationBuilder.DropColumn(
                name: "UnitaLocaleId",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "TipoAvvio",
                schema: "hr",
                table: "Aziende");
        }
    }
}
