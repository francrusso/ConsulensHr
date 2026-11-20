using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Organigramma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Posizione",
                schema: "hr",
                table: "Dipendenti",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponsabileId",
                schema: "hr",
                table: "Dipendenti",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                schema: "hr",
                table: "Dipendenti",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Team",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PadreId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsabileId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Team", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Team_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Team_Dipendenti_ResponsabileId",
                        column: x => x.ResponsabileId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Team_Team_PadreId",
                        column: x => x.PadreId,
                        principalSchema: "hr",
                        principalTable: "Team",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dipendenti_ResponsabileId",
                schema: "hr",
                table: "Dipendenti",
                column: "ResponsabileId");

            migrationBuilder.CreateIndex(
                name: "IX_Dipendenti_TeamId",
                schema: "hr",
                table: "Dipendenti",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Team_AziendaId_Nome",
                schema: "hr",
                table: "Team",
                columns: new[] { "AziendaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Team_PadreId",
                schema: "hr",
                table: "Team",
                column: "PadreId");

            migrationBuilder.CreateIndex(
                name: "IX_Team_ResponsabileId",
                schema: "hr",
                table: "Team",
                column: "ResponsabileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Dipendenti_Dipendenti_ResponsabileId",
                schema: "hr",
                table: "Dipendenti",
                column: "ResponsabileId",
                principalSchema: "hr",
                principalTable: "Dipendenti",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Dipendenti_Team_TeamId",
                schema: "hr",
                table: "Dipendenti",
                column: "TeamId",
                principalSchema: "hr",
                principalTable: "Team",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Dipendenti_Dipendenti_ResponsabileId",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropForeignKey(
                name: "FK_Dipendenti_Team_TeamId",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropTable(
                name: "Team",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "IX_Dipendenti_ResponsabileId",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropIndex(
                name: "IX_Dipendenti_TeamId",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "Posizione",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "ResponsabileId",
                schema: "hr",
                table: "Dipendenti");

            migrationBuilder.DropColumn(
                name: "TeamId",
                schema: "hr",
                table: "Dipendenti");
        }
    }
}
