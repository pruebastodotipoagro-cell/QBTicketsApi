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

            float pageHeight = Math.Min(420, Math.Max(210, 185 + (lines.Count * 9)));

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(80, pageHeight, Unit.Millimetre);
                    page.MarginHorizontal(3, Unit.Millimetre);
                    page.MarginTop(0, Unit.Millimetre);
                    page.MarginBottom(3, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(7.4f));

                    page.Content().Column(col =>
                    {
                        col.Spacing(1);

                        if (File.Exists(logoPath))
                        {
                            col.Item()
                                .AlignCenter()
                                .Width(28, Unit.Millimetre)
                                .Image(logoPath)
                                .FitWidth();
                        }

                        Space(col, 3);

                        col.Item().AlignCenter().Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA").Bold().FontSize(8.2f);
                        col.Item().AlignCenter().Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA, S.A.").Bold().FontSize(6.4f);
                        col.Item().AlignCenter().Text("NIT: 120074427").Bold().FontSize(6.8f);
                        col.Item().AlignCenter().Text("CARRETERA INTERAMERICANA, ZONA 0, ALDEA").FontSize(6.3f);
                        col.Item().AlignCenter().Text("TIUCAL, ASUNCIÓN MITA, JUTIAPA").FontSize(6.3f);

                        Space(col, 5);

                        col.Item().AlignCenter().Text("FACTURA").Bold().FontSize(9.5f);

                        Space(col, 3);

                        col.Item().AlignCenter().Text($"Serie: {fel.Serie}").Bold().FontSize(6.8f);
                        col.Item().AlignCenter().Text($"Número de DTE: {fel.DteNumber}").Bold().FontSize(6.8f);

                        Space(col, 4);

                        col.Item().AlignCenter().Text("No. Autorización:").Bold().FontSize(7.2f);
                        col.Item().AlignCenter().Text(fel.AuthorizationNumber).Bold().FontSize(6.2f);

                        Space(col, 3);

                        col.Item().AlignCenter().Text($"Fecha de Certificación: {certDateGuatemala:dd/MM/yyyy, HH:mm:ss}").Bold().FontSize(6.8f);
                        col.Item().AlignCenter().Text($"FECHA DE EMISIÓN: {date}").Bold().FontSize(6.8f);

                        Space(col, 3);

                        col.Item().Row(row =>
                        {
                            row.ConstantItem(13, Unit.Millimetre).Text("NIT:").Bold().FontSize(7.0f);
                            row.RelativeItem().Text(customerNit).FontSize(7.0f);
                        });

                        col.Item().Row(row =>
                        {
                            row.ConstantItem(13, Unit.Millimetre).Text("NOMBRE:").Bold().FontSize(7.0f);
                            row.RelativeItem().Text(customer.ToUpper()).FontSize(7.0f);
                        });

                        col.Item().Row(row =>
                        {
                            row.ConstantItem(25, Unit.Millimetre).Text("TIPO VENTA:").Bold().FontSize(7.0f);
                            row.RelativeItem().Text(tipoVentaTexto).FontSize(7.0f);
                        });

                        col.Item().Row(row =>
                        {
                            row.ConstantItem(25, Unit.Millimetre).Text("Factura No.:").Bold().FontSize(7.0f);
                            row.RelativeItem().Text($"#{docNumber}").FontSize(7.0f);
                        });

                        Space(col, 4);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(9, Unit.Millimetre);
                                columns.ConstantColumn(39, Unit.Millimetre);
                                columns.ConstantColumn(10, Unit.Millimetre);
                                columns.ConstantColumn(16, Unit.Millimetre);
                            });

                            table.Header(header =>
                            {
                                header.Cell().AlignCenter().Text("CANT").Bold().FontSize(6.7f);
                                header.Cell().AlignCenter().Text("DETALLE").Bold().FontSize(6.7f);
                                header.Cell().AlignRight().Text("Des.").Bold().FontSize(6.7f);
                                header.Cell().AlignRight().Text("TOTAL").Bold().FontSize(6.7f);

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

                                table.Cell().PaddingTop(4).AlignCenter().Text(qty.ToString("N0")).FontSize(6.4f);
                                table.Cell().PaddingTop(4).AlignCenter().Text(itemName.ToUpper()).Bold().FontSize(6.0f);
                                table.Cell().PaddingTop(4).AlignRight().Text("0.00").FontSize(6.2f);
                                table.Cell().PaddingTop(4).AlignRight().Text(totalLine(amount)).Bold().FontSize(6.2f);
                            }
                        });

                        Space(col, 7);
                        SolidLine(col);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignRight().Text("TOTAL:").Bold().FontSize(8.0f);
                            row.ConstantItem(22, Unit.Millimetre).AlignRight().Text("Q " + total.ToString("N2")).Bold().FontSize(8.0f);
                        });

                        Space(col, 4);

                        col.Item().Text("TOTAL DE LA FACTURA EN LETRAS:").Bold().FontSize(8.0f);
                        col.Item().Text(NumberToWords(total).ToUpper()).FontSize(7.0f);

                        Space(col, 5);

                        col.Item().AlignCenter().Text("SUJETO A PAGOS TRIMESTRALES ISR").Bold().FontSize(8.0f);
                        col.Item().Text($"CERTIFICADOR: {fel.CertifierName}").Bold().FontSize(7.0f);
                        col.Item().Text($"NIT: {fel.CertifierNit}").Bold().FontSize(7.0f);
                    });
                });
            }).GeneratePdf();
        }

        private static string totalLine(decimal amount)
        {
            return amount.ToString("N2");
        }

        private static void Space(ColumnDescriptor col, float millimetres)
        {
            col.Item().Height(millimetres, Unit.Millimetre);
        }

        private static void SolidLine(ColumnDescriptor col)
        {
            col.Item().LineHorizontal(0.5f);
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