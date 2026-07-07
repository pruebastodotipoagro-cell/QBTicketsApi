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
            var queryResponse = doc.RootElement.GetProperty("QueryResponse");

            JsonElement receipt;
            bool esContado;

            if (queryResponse.TryGetProperty("SalesReceipt", out var salesReceipts))
            {
                receipt = salesReceipts[0];
                esContado = true;
            }
            else if (queryResponse.TryGetProperty("Invoice", out var invoices))
            {
                receipt = invoices[0];
                esContado = false;
            }
            else
            {
                throw new Exception("QuickBooks no devolvió SalesReceipt ni Invoice.");
            }

            string docNumber = GetString(receipt, "DocNumber", "SIN-NUMERO");

            string rawDate = GetString(receipt, "TxnDate", DateTime.Now.ToString("yyyy-MM-dd"));
            string date = DateTime.TryParse(rawDate, out var parsedTxnDate)
                ? parsedTxnDate.ToString("dd/MM/yyyy")
                : rawDate;

            decimal total = GetDecimal(receipt, "TotalAmt");

            string customerNit = !string.IsNullOrWhiteSpace(fel.CustomerNit)
                ? fel.CustomerNit
                : GetString(receipt, "CustomerNit", "CF");

            string customer = "Consumidor Final";
            if (receipt.TryGetProperty("CustomerRef", out var customerRef))
                customer = GetString(customerRef, "name", "Consumidor Final");

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
                    page.Size(77, PageSizes.A4.Height, Unit.Millimetre);
                    page.Margin(2, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        col.Spacing(3);

                        if (File.Exists(logoPath))
                        {
                            col.Item()
                                .AlignCenter()
                                .Width(82)
                                .Image(logoPath)
                                .FitWidth();
                        }

                        col.Item().AlignCenter().Text("INNOVACIONES AGRÍCOLAS").Bold().FontSize(12);
                        col.Item().AlignCenter().Text("DE GUATEMALA").Bold().FontSize(12);
                        col.Item().AlignCenter().Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA, S.A.").Bold().FontSize(8);
                        col.Item().AlignCenter().Text("NIT: 120074427").Bold().FontSize(8);
                        col.Item().AlignCenter().Text("Carr. Interamericana, Zona 0, Aldea Tiucal").FontSize(8);
                        col.Item().AlignCenter().Text("Asunción Mita, Jutiapa").FontSize(8);
                        col.Item().AlignCenter().Text("Sujeto a pagos trimestrales ISR").Bold().FontSize(8);

                        Dashed(col);

                        col.Item().AlignCenter().Text("FACTURA").Bold().FontSize(14);
                        col.Item().AlignCenter().Text(esContado ? "CONTADO" : "CRÉDITO").Bold().FontSize(10);

                        col.Item().PaddingTop(3).Text($"Factura No.: #{docNumber}").Bold().FontSize(9);
                        col.Item().Text($"Fecha emisión: {date}").FontSize(9);
                        col.Item().Text($"Cliente: {customer}").FontSize(9);
                        col.Item().Text($"NIT: {customerNit}").FontSize(9);

                        Dashed(col);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(24);
                                columns.RelativeColumn();
                                columns.ConstantColumn(43);
                                columns.ConstantColumn(50);
                            });

                            table.Header(header =>
                            {
                                header.Cell().AlignLeft().Text("CANT").Bold().FontSize(8);
                                header.Cell().AlignCenter().Text("DETALLE").Bold().FontSize(8);
                                header.Cell().AlignRight().Text("Des.").Bold().FontSize(8);
                                header.Cell().AlignRight().Text("TOTAL").Bold().FontSize(8);

                                header.Cell().ColumnSpan(4).PaddingTop(2).LineHorizontal(0.5f);
                            });

                            foreach (var line in lines)
                            {
                                var detail = line.GetProperty("SalesItemLineDetail");

                                decimal qty = GetDecimal(detail, "Qty", 1);
                                decimal amount = GetDecimal(line, "Amount");

                                string itemName = "Producto";
                                if (detail.TryGetProperty("ItemRef", out var itemRef))
                                    itemName = GetString(itemRef, "name", "Producto");

                                table.Cell().PaddingTop(5).AlignCenter().Text(qty.ToString("N0")).FontSize(8);
                                table.Cell().PaddingTop(5).AlignCenter().Text(itemName.ToUpper()).Bold().FontSize(8);
                                table.Cell().PaddingTop(5).AlignRight().Text("Q 0.00").FontSize(8);
                                table.Cell().PaddingTop(5).AlignRight().Text("Q " + amount.ToString("N2")).Bold().FontSize(8);
                            }
                        });

                        Dashed(col);

                        col.Item().PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("TOTAL:").Bold().FontSize(15);
                            row.RelativeItem().AlignRight().Text("Q " + total.ToString("N2")).Bold().FontSize(15);
                        });

                        col.Item()
                            .PaddingTop(6)
                            .AlignCenter()
                            .Text(NumberToWords(total).ToUpper())
                            .Bold()
                            .FontSize(8);

                        Dashed(col);

                        col.Item().AlignCenter().Text("DATOS FEL").Bold().FontSize(10);
                        col.Item().AlignCenter().Text($"Serie: {fel.Serie}").FontSize(8);
                        col.Item().AlignCenter().Text($"Número de DTE: {fel.DteNumber}").FontSize(8);

                        col.Item().PaddingTop(4).AlignCenter().Text("No. Autorización:").Bold().FontSize(9);
                        col.Item().AlignCenter().Text(fel.AuthorizationNumber).Bold().FontSize(7);

                        col.Item().PaddingTop(4).AlignCenter()
                            .Text($"Fecha de Certificación: {certDateGuatemala:dd/MM/yyyy HH:mm}")
                            .FontSize(8);

                        col.Item().AlignCenter().Text($"FECHA DE EMISION: {date}").Bold().FontSize(8);
                        col.Item().AlignCenter().Text("CERTIFICADOR: MEGAPRINT, S.A.").Bold().FontSize(8);
                        col.Item().AlignCenter().Text("NIT: 50510231").Bold().FontSize(8);

                        Dashed(col);

                        col.Item().AlignCenter().Text("¡Gracias por su preferencia!").Bold().FontSize(10);
                        col.Item().AlignCenter()
                            .Text("Contribuyendo al desarrollo agrícola de Guatemala.")
                            .Bold()
                            .FontSize(7);
                    });
                });
            }).GeneratePdf();
        }

        private static void Dashed(ColumnDescriptor col)
        {
            col.Item()
                .PaddingVertical(4)
                .AlignCenter()
                .Text("------------------------------------------------------------")
                .FontSize(8);
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