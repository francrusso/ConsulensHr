using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TavolozzaChiara : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Colore",
                schema: "hr",
                table: "Team",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#C9B6F2",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#A47FE6");

            // I team con un colore della vecchia tavolozza passano al corrispondente più chiaro (i colori personalizzati restano).
            migrationBuilder.Sql("""
                UPDATE hr."Team" SET "Colore" = '#C9B6F2' WHERE upper("Colore") = '#A47FE6';
                UPDATE hr."Team" SET "Colore" = '#B8DDB6' WHERE upper("Colore") = '#8CC98A';
                UPDATE hr."Team" SET "Colore" = '#F7C9B0' WHERE upper("Colore") = '#F4A582';
                UPDATE hr."Team" SET "Colore" = '#B5CCFB' WHERE upper("Colore") = '#6096FF';
                UPDATE hr."Team" SET "Colore" = '#F5E1A0' WHERE upper("Colore") = '#F2C14E';
                UPDATE hr."Team" SET "Colore" = '#F2C0D4' WHERE upper("Colore") = '#EC8FB5';
                UPDATE hr."Team" SET "Colore" = '#ADE0E2' WHERE upper("Colore") = '#4FBFC4';
                UPDATE hr."Team" SET "Colore" = '#DDB3B6' WHERE upper("Colore") = '#98494E';
                UPDATE hr."Team" SET "Colore" = '#D3D7DD' WHERE upper("Colore") = '#9AA3AE';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Colore",
                schema: "hr",
                table: "Team",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#A47FE6",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldDefaultValue: "#C9B6F2");
        }
    }
}
