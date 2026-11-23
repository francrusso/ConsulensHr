using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SmartWorkingConcesso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GiorniSmartWorking",
                schema: "hr",
                table: "Contratti",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeriodoSmartWorking",
                schema: "hr",
                table: "Contratti",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GiorniSmartWorking",
                schema: "hr",
                table: "Contratti");

            migrationBuilder.DropColumn(
                name: "PeriodoSmartWorking",
                schema: "hr",
                table: "Contratti");
        }
    }
}
