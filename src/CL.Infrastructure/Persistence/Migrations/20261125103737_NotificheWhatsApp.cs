using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificheWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WhatsApp",
                schema: "hr",
                table: "PreferenzeNotifiche",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppConsensoIl",
                schema: "hr",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WhatsApp",
                schema: "hr",
                table: "PreferenzeNotifiche");

            migrationBuilder.DropColumn(
                name: "WhatsAppConsensoIl",
                schema: "hr",
                table: "AspNetUsers");
        }
    }
}
