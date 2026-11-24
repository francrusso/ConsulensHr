using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TurniLavoro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TurniLavoro",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Inizio = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Fine = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurniLavoro", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TurniLavoro_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TurniLavoro_DipendenteId_Inizio_Fine",
                schema: "hr",
                table: "TurniLavoro",
                columns: new[] { "DipendenteId", "Inizio", "Fine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurniLavoro",
                schema: "hr");
        }
    }
}
