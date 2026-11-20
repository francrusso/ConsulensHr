using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChiusureAziendali : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChiusuraId",
                schema: "hr",
                table: "RichiesteAssenza",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChiusureAziendali",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dal = table.Column<DateOnly>(type: "date", nullable: false),
                    Al = table.Column<DateOnly>(type: "date", nullable: false),
                    Descrizione = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreataDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChiusureAziendali", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChiusureAziendali_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChiusureAziendali_AziendaId_Dal",
                schema: "hr",
                table: "ChiusureAziendali",
                columns: new[] { "AziendaId", "Dal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChiusureAziendali",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "ChiusuraId",
                schema: "hr",
                table: "RichiesteAssenza");
        }
    }
}
