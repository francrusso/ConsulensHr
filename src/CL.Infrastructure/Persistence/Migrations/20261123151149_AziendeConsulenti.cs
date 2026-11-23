using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AziendeConsulenti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AziendeConsulenti",
                schema: "hr",
                columns: table => new
                {
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AssegnataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AziendeConsulenti", x => new { x.AziendaId, x.UserId });
                    table.ForeignKey(
                        name: "FK_AziendeConsulenti_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AziendeConsulenti_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AziendeConsulenti_UserId",
                schema: "hr",
                table: "AziendeConsulenti",
                column: "UserId");

            // Per non togliere niente a nessuno: le aziende già esistenti restano visibili a tutti i consulenti di oggi.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                migrationBuilder.Sql("""
                    INSERT INTO hr."AziendeConsulenti" ("AziendaId", "UserId", "AssegnataIl")
                    SELECT a."Id", ur."UserId", now() at time zone 'utc'
                    FROM hr."Aziende" a
                    CROSS JOIN hr."AspNetUserRoles" ur
                    JOIN hr."AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE r."Name" = 'Consulente'
                    ON CONFLICT DO NOTHING;
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AziendeConsulenti",
                schema: "hr");
        }
    }
}
