using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QBTicketsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCertifierFieldsToInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FelCertifierName",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FelCertifierNit",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FelCertifierName",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FelCertifierNit",
                table: "Invoices");
        }
    }
}
