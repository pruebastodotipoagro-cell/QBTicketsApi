using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QBTicketsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLinesDiscountsAndPriceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditPercentage",
                table: "Invoices",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountTotal",
                table: "Invoices",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PriceType",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "Invoices",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    QuickBooksLineId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QuickBooksItemId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    OriginalUnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    AppliedUnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    OriginalSubtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    FinalTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuickBooksId",
                table: "Invoices",
                column: "QuickBooksId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuickBooksId_CreatedAt",
                table: "Invoices",
                columns: new[] { "QuickBooksId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashierName_MovementDate_MovementType",
                table: "CashMovements",
                columns: new[] { "CashierName", "MovementDate", "MovementType" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId_QuickBooksLineId",
                table: "InvoiceLines",
                columns: new[] { "InvoiceId", "QuickBooksLineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_QuickBooksId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_QuickBooksId_CreatedAt",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashierName_MovementDate_MovementType",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "CreditPercentage",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscountTotal",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PriceType",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "Invoices");
        }
    }
}
