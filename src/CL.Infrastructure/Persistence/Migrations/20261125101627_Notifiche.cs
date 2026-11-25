using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifiche : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifiche",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UtenteId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Titolo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Testo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Chiave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LettaIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifiche", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifiche_AspNetUsers_UtenteId",
                        column: x => x.UtenteId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreferenzeNotifiche",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UtenteId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Email = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferenzeNotifiche", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreferenzeNotifiche_AspNetUsers_UtenteId",
                        column: x => x.UtenteId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifiche_Chiave_UtenteId",
                schema: "hr",
                table: "Notifiche",
                columns: new[] { "Chiave", "UtenteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifiche_UtenteId_CreataIl",
                schema: "hr",
                table: "Notifiche",
                columns: new[] { "UtenteId", "CreataIl" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifiche_UtenteId_LettaIl",
                schema: "hr",
                table: "Notifiche",
                columns: new[] { "UtenteId", "LettaIl" });

            migrationBuilder.CreateIndex(
                name: "IX_PreferenzeNotifiche_UtenteId_Tipo",
                schema: "hr",
                table: "PreferenzeNotifiche",
                columns: new[] { "UtenteId", "Tipo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifiche",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "PreferenzeNotifiche",
                schema: "hr");
        }
    }
}
