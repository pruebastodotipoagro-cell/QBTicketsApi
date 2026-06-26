using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace QBTicketsApi.Services
{
    public class TicketPdfService
    {
        public byte[] GenerateSalesReceiptPdf(string json, FelResult fel)
        {
            using var doc = JsonDocument.Parse(json);

            var receipt = doc.RootElement
                .GetProperty("QueryResponse")
                .GetProperty("SalesReceipt")[0];

            string docNumber = GetString(receipt, "DocNumber", "SIN-NUMERO");
            string date = GetString(receipt, "TxnDate", DateTime.Now.ToString("yyyy-MM-dd"));
            decimal total = GetDecimal(receipt, "TotalAmt");

            string customer = "Consumidor Final";
            if (receipt.TryGetProperty("CustomerRef", out var customerRef))
                customer = GetString(customerRef, "name", "Consumidor Final");

            var lines = receipt.GetProperty("Line")
                .EnumerateArray()
                .Where(x => x.TryGetProperty("SalesItemLineDetail", out _))
                .ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(77, PageSizes.A4.Height, Unit.Millimetre);
                    page.Margin(2, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        col.Spacing(4);

                        col.Item().AlignCenter().Text("INNOVACIONES AGRÍCOLAS").Bold().FontSize(12);
                        col.Item().AlignCenter().Text("DE GUATEMALA").Bold().FontSize(12);
                        col.Item().AlignCenter().Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA, S.A.").Bold().FontSize(8);
                        col.Item().AlignCenter().Text("NIT: 120074427").FontSize(8);
                        col.Item().AlignCenter().Text("Carr. Interamericana, Zona 0, Aldea Tiucal").FontSize(8);
                        col.Item().AlignCenter().Text("Asunción Mita, Jutiapa").FontSize(8);
                        col.Item().AlignCenter().Text("Sujeto a pagos trimestrales ISR").FontSize(8);

                        Divider(col);

                        col.Item().AlignCenter().Text("FACTURA").Bold().FontSize(12);

                        col.Item().Text($"Factura No.: #{docNumber}").Bold();
                        col.Item().Text($"Fecha emisión: {date}");
                        col.Item().Text($"Cliente: {customer}");
                        col.Item().Text("NIT: CF");



                        Divider(col);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(5);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("CANT").Bold().FontSize(8);
                                header.Cell().Text("DETALLE").Bold().FontSize(8);
                                header.Cell().AlignRight().Text("TOTAL").Bold().FontSize(8);
                            });

                            foreach (var line in lines)
                            {
                                var detail = line.GetProperty("SalesItemLineDetail");

                                decimal qty = GetDecimal(detail, "Qty", 1);
                                decimal amount = GetDecimal(line, "Amount");

                                string itemName = "Producto";

                                if (detail.TryGetProperty("ItemRef", out var itemRef))
                                    itemName = GetString(itemRef, "name", "Producto");

                                table.Cell().Text(qty.ToString("N0")).FontSize(8);
                                table.Cell().Text(itemName).Bold().FontSize(8);
                                table.Cell().AlignRight().Text("Q " + amount.ToString("N2")).Bold().FontSize(8);
                            }
                        });

                        Divider(col);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("TOTAL:").Bold().FontSize(12);
                            row.RelativeItem().AlignRight().Text("Q " + total.ToString("N2")).Bold().FontSize(12);
                        });

                        Divider(col);

                        col.Item().Text("DATOS FEL").Bold().FontSize(11);

                        col.Item().Text($"Serie: {fel.Serie}").FontSize(8);

                        col.Item().Text($"Número DTE: {fel.DteNumber}").FontSize(8);

                        col.Item().Text($"Fecha certificación: {fel.CertificationDate:dd/MM/yyyy HH:mm}")
                            .FontSize(8);

                        col.Item().Text("Número de autorización:")
                            .Bold()
                            .FontSize(8);

                        col.Item().Text(fel.AuthorizationNumber)
                            .FontSize(7);

                        col.Item().AlignCenter().Text("¡Gracias por su preferencia!").Bold().FontSize(10);
                        col.Item().AlignCenter().Text("Contribuyendo al desarrollo agrícola de Guatemala.").FontSize(8);
                    });
                });
            }).GeneratePdf();
        }

        private static void Divider(ColumnDescriptor col)
        {
            col.Item().PaddingVertical(3).LineHorizontal(0.5f);
        }

        private static string GetString(JsonElement element, string property, string fallback = "")
        {
            return element.TryGetProperty(property, out var value)
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static decimal GetDecimal(JsonElement element, string property, decimal fallback = 0)
        {
            if (!element.TryGetProperty(property, out var value)) return fallback;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result))
                return result;

            return fallback;
        }
    }
}