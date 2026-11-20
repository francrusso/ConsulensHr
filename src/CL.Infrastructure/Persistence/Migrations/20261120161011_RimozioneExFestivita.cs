using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RimozioneExFestivita : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Le ex-festività non esistono più: quanto già registrato confluisce nei ROL, così nessuna ora va persa.
            migrationBuilder.Sql("""
                UPDATE hr."RighePresenze" SET "OreRol" = "OreRol" + "OreExFestivita" WHERE "OreExFestivita" <> 0;
                UPDATE hr."RichiesteAssenza" SET "Tipo" = 'Rol' WHERE "Tipo" = 'ExFestivita';
                UPDATE hr."SaldiIniziali" r SET "Ore" = r."Ore" + e."Ore"
                    FROM hr."SaldiIniziali" e
                    WHERE r."Tipo" = 'Rol' AND e."Tipo" = 'ExFestivita' AND e."DipendenteId" = r."DipendenteId" AND e."Anno" = r."Anno";
                UPDATE hr."SaldiIniziali" e SET "Tipo" = 'Rol'
                    WHERE e."Tipo" = 'ExFestivita' AND NOT EXISTS (SELECT 1 FROM hr."SaldiIniziali" r
                        WHERE r."Tipo" = 'Rol' AND r."DipendenteId" = e."DipendenteId" AND r."Anno" = e."Anno");
                DELETE FROM hr."SaldiIniziali" WHERE "Tipo" = 'ExFestivita';
                """);

            migrationBuilder.DropColumn(
                name: "OreExFestivita",
                schema: "hr",
                table: "RighePresenze");

            migrationBuilder.DropColumn(
                name: "OreExFestivitaAnnue",
                schema: "hr",
                table: "Contratti");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OreExFestivita",
                schema: "hr",
                table: "RighePresenze",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OreExFestivitaAnnue",
                schema: "hr",
                table: "Contratti",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
