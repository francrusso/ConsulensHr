using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TurnazioneSettimanale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Nome",
                schema: "hr",
                table: "TurniLavoro",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TurniSalvati",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Inizio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Fine = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurniSalvati", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TurniSalvati_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TurniSalvati_AziendaId_Nome",
                schema: "hr",
                table: "TurniSalvati",
                columns: new[] { "AziendaId", "Nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TurniSalvati",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "Nome",
                schema: "hr",
                table: "TurniLavoro");
        }
    }
}
