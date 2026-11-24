using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamMultipliTurni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VincoliTeam",
                schema: "hr",
                table: "TurniLavoro",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamAbilitatiTurni",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnoSalvatoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAbilitatiTurni", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamAbilitatiTurni_TurniSalvati_TurnoSalvatoId",
                        column: x => x.TurnoSalvatoId,
                        principalSchema: "hr",
                        principalTable: "TurniSalvati",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamAbilitatiTurni_TurnoSalvatoId_TeamId",
                schema: "hr",
                table: "TeamAbilitatiTurni",
                columns: new[] { "TurnoSalvatoId", "TeamId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamAbilitatiTurni",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "VincoliTeam",
                schema: "hr",
                table: "TurniLavoro");
        }
    }
}
