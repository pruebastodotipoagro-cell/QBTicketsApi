using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QBTicketsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFelFieldsToInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FelAuthorizationNumber",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FelCertificationDate",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FelDteNumber",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FelQr",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FelSerie",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsCertified",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FelAuthorizationNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FelCertificationDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FelDteNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FelQr",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FelSerie",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsCertified",
                table: "Invoices");
        }
    }
}
