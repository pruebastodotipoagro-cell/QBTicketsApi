using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace QBTicketsApi.Services
{
    public class TicketPdfService
    {
        public byte[] GenerateSalesReceiptPdf(string json, FelResult fel, string saleType)
        {
            using var doc = JsonDocument.Parse(json);
            var queryResponse = doc.RootElement.GetProperty("QueryResponse");

            JsonElement receipt;

            if (queryResponse.TryGetProperty("SalesReceipt", out var salesReceipts))
                receipt = salesReceipts[0];
            else if (queryResponse.TryGetProperty("Invoice", out var invoices))
                receipt = invoices[0];
            else
                throw new Exception("QuickBooks no devolvió SalesReceipt ni Invoice.");

            string docNumber = GetString(receipt, "DocNumber", "SIN-NUMERO");

            string rawDate = GetString(receipt, "TxnDate", DateTime.Now.ToString("yyyy-MM-dd"));
            string date = DateTime.TryParse(rawDate, out var parsedTxnDate)
                ? parsedTxnDate.ToString("dd/MM/yyyy HH:mm")
                : rawDate;

            decimal total = GetDecimal(receipt, "TotalAmt");

            string customerNit = !string.IsNullOrWhiteSpace(fel.CustomerNit)
                ? fel.CustomerNit
                : GetString(receipt, "CustomerNit", "CF");

            string customer = "Consumidor Final";
            if (receipt.TryGetProperty("CustomerRef", out var customerRef))
                customer = GetString(customerRef, "name", "Consumidor Final");

            string tipoVentaTexto = saleType?.ToLower() == "contado" ? "CONTADO" : "CRÉDITO";

            var lines = receipt.GetProperty("Line")
                .EnumerateArray()
                .Where(x => x.TryGetProperty("SalesItemLineDetail", out _))
                .ToList();

            var certDateGuatemala = fel.CertificationDate.Kind == DateTimeKind.Utc
                ? TimeZoneInfo.ConvertTimeFromUtc(
                    fel.CertificationDate,
                    TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala"))
                : fel.CertificationDate;

            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Logo INNOVACIONES.jpeg");

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(80, 190, Unit.Millimetre);
                    page.MarginHorizontal(3, Unit.Millimetre);
                    page.MarginTop(0, Unit.Millimetre);
                    page.MarginBottom(4, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(7.6f).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        col.Spacing(4);

                        if (File.Exists(logoPath))
                        {
                            col.Item()
                                .AlignCenter()
                                .Width(42, Unit.Millimetre)
                                .Image(logoPath)
                                .FitWidth();
                        }

                        col.Item().PaddingTop(2).AlignCenter().Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA").Bold().FontSize(9.5f);
                        col.Item().AlignCenter().Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA, S.A.").Bold().FontSize(6.8f);
                        col.Item().AlignCenter().Text("NIT: 120074427").Bold().FontSize(7.6f);
                        col.Item().AlignCenter().Text("Carr. Interamericana, Zona 0, Aldea Tiucal").FontSize(6.8f);
                        col.Item().AlignCenter().Text("Asunción Mita, Jutiapa").FontSize(6.8f);
                        col.Item().AlignCenter().Text("Sujeto a pagos trimestrales ISR").Bold().FontSize(6.8f);

                        Dashed(col);

                        col.Item().AlignCenter().Text("FACTURA").Bold().FontSize(11.4f);

                        col.Item().PaddingTop(2).Text($"Factura No.: #{docNumber}").Bold().FontSize(7.6f);
                        col.Item().PaddingTop(2).Text($"Fecha emisión: {date}").FontSize(7.6f);
                        col.Item().PaddingTop(2).Text($"Tipo de venta: {tipoVentaTexto}").Bold().FontSize(7.6f);
                        col.Item().PaddingTop(3).Text($"NIT: {customerNit}").FontSize(7.6f);
                        col.Item().PaddingTop(2).Text($"Cliente: {customer}").FontSize(7.6f);

                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(26);
                                columns.RelativeColumn();
                                columns.ConstantColumn(34);
                                columns.ConstantColumn(45);
                            });

                            table.Header(header =>
                            {
                                header.Cell().AlignCenter().Text("CANT").Bold().FontSize(6.7f);
                                header.Cell().AlignCenter().Text("DETALLE").Bold().FontSize(6.7f);
                                header.Cell().AlignRight().Text("Des.").Bold().FontSize(6.7f);
                                header.Cell().AlignRight().Text("TOTAL").Bold().FontSize(6.7f);

                                header.Cell()
                                    .ColumnSpan(4)
                                    .PaddingTop(2)
                                    .LineHorizontal(0.5f);
                            });

                            foreach (var line in lines)
                            {
                                var detail = line.GetProperty("SalesItemLineDetail");

                                decimal qty = GetDecimal(detail, "Qty", 1);
                                decimal amount = GetDecimal(line, "Amount");

                                string itemName = "Producto";
                                if (detail.TryGetProperty("ItemRef", out var itemRef))
                                    itemName = GetString(itemRef, "name", "Producto");

                                table.Cell().PaddingTop(5).AlignCenter().Text(qty.ToString("N0")).FontSize(6.7f);
                                table.Cell().PaddingTop(5).AlignCenter().Text(itemName.ToUpper()).Bold().FontSize(6.35f);
                                table.Cell().PaddingTop(5).AlignRight().Text("0.00").FontSize(6.7f);
                                table.Cell().PaddingTop(5).AlignRight().Text("Q " + amount.ToString("N2")).Bold().FontSize(6.7f);
                            }
                        });

                        Dashed(col);

                        col.Item().PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem().AlignLeft().Text("TOTAL:").Bold().FontSize(8.6f);
                            row.RelativeItem().AlignRight().Text("Q " + total.ToString("N2")).Bold().FontSize(8.6f);
                        });

                        col.Item()
                            .PaddingTop(5)
                            .AlignCenter()
                            .Text(NumberToWords(total).ToUpper())
                            .Bold()
                            .FontSize(7.6f);

                        Dashed(col);

                        col.Item().PaddingTop(3).Text($"Serie: {fel.Serie}").Bold().FontSize(7.6f);
                        col.Item().PaddingTop(3).Text($"Número de DTE: {fel.DteNumber}").Bold().FontSize(7.6f);

                        col.Item().PaddingTop(6).AlignCenter().Text("No. Autorización:").Bold().FontSize(7.6f);
                        col.Item().PaddingTop(2).AlignCenter().Text(fel.AuthorizationNumber).Bold().FontSize(6.7f);

                        col.Item().PaddingTop(6).Text($"Fecha de Certificación: {certDateGuatemala:dd/MM/yyyy HH:mm}").Bold().FontSize(7.6f);
                        col.Item().PaddingTop(3).Text($"CERTIFICADOR: {fel.CertifierName}").Bold().FontSize(7.6f);
                        col.Item().PaddingTop(3).Text($"NIT: {fel.CertifierNit}").Bold().FontSize(7.6f);

                        Dashed(col);

                        col.Item().PaddingTop(4).AlignCenter().Text("¡Gracias por su preferencia!").Bold().FontSize(8.6f);
                        col.Item().AlignCenter().Text("Contribuyendo al desarrollo agrícola de Guatemala.").Bold().FontSize(6.7f);
                    });
                });
            }).GeneratePdf();
        }

        private static void Dashed(ColumnDescriptor col)
        {
            col.Item().PaddingVertical(5).Row(row =>
            {
                const float dashWidth = 1f;
                const float gapWidth = 1f;
                const float totalWidth = 74f;
                int segments = (int)(totalWidth / (dashWidth + gapWidth));

                for (int i = 0; i < segments; i++)
                {
                    row.ConstantItem(dashWidth, Unit.Millimetre)
                        .Height(0.3f, Unit.Millimetre)
                        .Background(Colors.Black);

                    row.ConstantItem(gapWidth, Unit.Millimetre);
                }
            });
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

        private static string NumberToWords(decimal amount)
        {
            int quetzales = (int)Math.Floor(amount);
            int centavos = (int)Math.Round((amount - quetzales) * 100);

            return $"{ToWords(quetzales)} QUETZALES CON {centavos:00}/100";
        }

        private static string ToWords(int number)
        {
            if (number == 0) return "CERO";

            string[] units =
            {
                "", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
                "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE",
                "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE"
            };

            string[] tens =
            {
                "", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA",
                "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"
            };

            string[] hundreds =
            {
                "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS",
                "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"
            };

            if (number == 100) return "CIEN";
            if (number < 20) return units[number];

            if (number < 30)
                return number == 20 ? "VEINTE" : "VEINTI" + units[number - 20].ToLower();

            if (number < 100)
                return tens[number / 10] + (number % 10 > 0 ? " Y " + units[number % 10] : "");

            if (number < 1000)
                return hundreds[number / 100] + (number % 100 > 0 ? " " + ToWords(number % 100) : "");

            if (number < 1000000)
            {
                int thousands = number / 1000;
                int rest = number % 1000;

                string thousandText = thousands == 1 ? "MIL" : ToWords(thousands) + " MIL";
                return thousandText + (rest > 0 ? " " + ToWords(rest) : "");
            }

            return number.ToString();
        }
    }
}