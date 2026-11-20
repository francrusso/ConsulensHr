using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GiorniInUfficio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GiorniInUfficio",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Giorno = table.Column<DateOnly>(type: "date", nullable: false),
                    SegnatoIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiorniInUfficio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiorniInUfficio_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiorniInUfficio_DipendenteId_Giorno",
                schema: "hr",
                table: "GiorniInUfficio",
                columns: new[] { "DipendenteId", "Giorno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiorniInUfficio_Giorno",
                schema: "hr",
                table: "GiorniInUfficio",
                column: "Giorno");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiorniInUfficio",
                schema: "hr");
        }
    }
}
