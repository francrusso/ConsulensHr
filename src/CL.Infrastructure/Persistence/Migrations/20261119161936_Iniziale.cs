using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Iniziale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hr");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Aziende",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RagioneSociale = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PartitaIva = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    CodiceFiscale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Indirizzo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Comune = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SantoPatronoMese = table.Column<int>(type: "integer", nullable: true),
                    SantoPatronoGiorno = table.Column<int>(type: "integer", nullable: true),
                    Attiva = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aziende", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "hr",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cognome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: true),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attivo = table.Column<bool>(type: "boolean", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "hr",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "hr",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "hr",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "hr",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dipendenti",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cognome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CodiceFiscale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DataNascita = table.Column<DateOnly>(type: "date", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Telefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Indirizzo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    Matricola = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dipendenti", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dipendenti_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "hr",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dipendenti_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BustePaga",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AziendaId = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anno = table.Column<int>(type: "integer", nullable: false),
                    Mese = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Lordo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Netto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    NomeFile = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ChiaveArchivio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    HashSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DimensioneByte = table.Column<long>(type: "bigint", nullable: false),
                    Stato = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CaricataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PubblicataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VisualizzataIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BustePaga", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BustePaga_Aziende_AziendaId",
                        column: x => x.AziendaId,
                        principalSchema: "hr",
                        principalTable: "Aziende",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BustePaga_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contratti",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ccnl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Livello = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mansione = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DataInizio = table.Column<DateOnly>(type: "date", nullable: false),
                    DataFine = table.Column<DateOnly>(type: "date", nullable: true),
                    OreSettimanaliFullTime = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PercentualePartTime = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    GiorniLavorativiSettimana = table.Column<int>(type: "integer", nullable: false),
                    RetribuzioneLordaAnnua = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    MensilitaAnnue = table.Column<int>(type: "integer", nullable: false),
                    OreFerieAnnue = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    OreRolAnnue = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    OreExFestivitaAnnue = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contratti", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contratti_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RichiesteAssenza",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Dal = table.Column<DateOnly>(type: "date", nullable: false),
                    Al = table.Column<DateOnly>(type: "date", nullable: false),
                    AOre = table.Column<bool>(type: "boolean", nullable: false),
                    Ore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProtocolloCertificato = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Stato = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RichiestaIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecisaDa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DecisaIl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoRifiuto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RichiesteAssenza", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RichiesteAssenza_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaldiIniziali",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DipendenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anno = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaldiIniziali", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaldiIniziali_Dipendenti_DipendenteId",
                        column: x => x.DipendenteId,
                        principalSchema: "hr",
                        principalTable: "Dipendenti",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "hr",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "hr",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "hr",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "hr",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "hr",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "hr",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_AziendaId",
                schema: "hr",
                table: "AspNetUsers",
                column: "AziendaId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DipendenteId",
                schema: "hr",
                table: "AspNetUsers",
                column: "DipendenteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "hr",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Aziende_PartitaIva",
                schema: "hr",
                table: "Aziende",
                column: "PartitaIva",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BustePaga_AziendaId_Anno_Mese",
                schema: "hr",
                table: "BustePaga",
                columns: new[] { "AziendaId", "Anno", "Mese" });

            migrationBuilder.CreateIndex(
                name: "IX_BustePaga_DipendenteId_Anno_Mese",
                schema: "hr",
                table: "BustePaga",
                columns: new[] { "DipendenteId", "Anno", "Mese" });

            migrationBuilder.CreateIndex(
                name: "IX_BustePaga_HashSha256",
                schema: "hr",
                table: "BustePaga",
                column: "HashSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contratti_DipendenteId",
                schema: "hr",
                table: "Contratti",
                column: "DipendenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Dipendenti_AziendaId_CodiceFiscale",
                schema: "hr",
                table: "Dipendenti",
                columns: new[] { "AziendaId", "CodiceFiscale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dipendenti_AziendaId_Cognome_Nome",
                schema: "hr",
                table: "Dipendenti",
                columns: new[] { "AziendaId", "Cognome", "Nome" });

            migrationBuilder.CreateIndex(
                name: "IX_Dipendenti_UserId",
                schema: "hr",
                table: "Dipendenti",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteAssenza_DipendenteId_Dal_Al",
                schema: "hr",
                table: "RichiesteAssenza",
                columns: new[] { "DipendenteId", "Dal", "Al" });

            migrationBuilder.CreateIndex(
                name: "IX_RichiesteAssenza_Stato",
                schema: "hr",
                table: "RichiesteAssenza",
                column: "Stato");

            migrationBuilder.CreateIndex(
                name: "IX_SaldiIniziali_DipendenteId_Anno_Tipo",
                schema: "hr",
                table: "SaldiIniziali",
                columns: new[] { "DipendenteId", "Anno", "Tipo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "BustePaga",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Contratti",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "RichiesteAssenza",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "SaldiIniziali",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Dipendenti",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Aziende",
                schema: "hr");
        }
    }
}
