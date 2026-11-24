using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LettereContratto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LettereContratto",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DataEffetto = table.Column<DateOnly>(type: "date", nullable: false),
                    Chiave = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NomeFile = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CaricataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContrattoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LettereContratto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LettereContratto_Contratti_ContrattoId",
                        column: x => x.ContrattoId,
                        principalSchema: "hr",
                        principalTable: "Contratti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LettereContratto_ContrattoId",
                schema: "hr",
                table: "LettereContratto",
                column: "ContrattoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LettereContratto",
                schema: "hr");
        }
    }
}
