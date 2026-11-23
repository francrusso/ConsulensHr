using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GiorniSmartWorking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GiorniSmartWorking",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Giorno = table.Column<DateOnly>(type: "date", nullable: false),
                    Stato = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RichiestoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecisoDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DecisoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoRifiuto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiorniSmartWorking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiorniSmartWorking_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiorniSmartWorking_DipendenteId_Giorno",
                schema: "hr",
                table: "GiorniSmartWorking",
                columns: new[] { "DipendenteId", "Giorno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiorniSmartWorking_Stato_Giorno",
                schema: "hr",
                table: "GiorniSmartWorking",
                columns: new[] { "Stato", "Giorno" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiorniSmartWorking",
                schema: "hr");
        }
    }
}
