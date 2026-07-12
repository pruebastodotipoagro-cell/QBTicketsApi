using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QBTicketsApi.DTOs;
using System.Globalization;
using System.Text.Json;

namespace QBTicketsApi.Services
{
    public class TicketPdfService
    {
        /*
         * Método anterior conservado para que las impresiones normales,
         * sin descuentos, sigan funcionando.
         */
        public byte[] GenerateSalesReceiptPdf(
            string json,
            FelResult fel,
            string saleType)
        {
            return GenerateSalesReceiptPdf(
                json,
                fel,
                saleType,
                null,
                new List<ItemDiscountRequest>()
            );
        }

        /*
         * Nuevo método para imprimir descuentos por producto.
         */
        public byte[] GenerateSalesReceiptPdf(
            string json,
            FelResult fel,
            string saleType,
            string? customerNameOverride,
            IReadOnlyCollection<ItemDiscountRequest>? discounts)
        {
            using var doc = JsonDocument.Parse(json);

            var queryResponse =
                doc.RootElement.GetProperty("QueryResponse");

            JsonElement receipt;

            if (queryResponse.TryGetProperty(
                "SalesReceipt",
                out var salesReceipts))
            {
                receipt = salesReceipts[0];
            }
            else if (queryResponse.TryGetProperty(
                "Invoice",
                out var invoices))
            {
                receipt = invoices[0];
            }
            else
            {
                throw new Exception(
                    "QuickBooks no devolvió SalesReceipt ni Invoice."
                );
            }

            string rawDate = GetString(
                receipt,
                "TxnDate",
                DateTime.Now.ToString("yyyy-MM-dd")
            );

            string date =
                DateTime.TryParse(rawDate, out var parsedTxnDate)
                    ? parsedTxnDate.ToString("dd/MM/yyyy HH:mm")
                    : rawDate;

            string customerNit =
                !string.IsNullOrWhiteSpace(fel.CustomerNit)
                    ? fel.CustomerNit
                    : GetString(receipt, "CustomerNit", "CF");

            string customer = "Consumidor Final";

            if (receipt.TryGetProperty(
                "CustomerRef",
                out var customerRef))
            {
                customer = GetString(
                    customerRef,
                    "name",
                    "Consumidor Final"
                );
            }

            if (!string.IsNullOrWhiteSpace(customerNameOverride))
            {
                customer = customerNameOverride.Trim();
            }

            if (customerNit.Equals(
                "CF",
                StringComparison.OrdinalIgnoreCase))
            {
                customer = "Consumidor Final";
            }

            string tipoVentaTexto =
                saleType?.ToLower() == "contado"
                    ? "CONTADO"
                    : "CRÉDITO";

            var discountMap =
                CrearMapaDescuentos(discounts);

            var ticketLines =
                ObtenerLineasTicket(receipt, discountMap);

            decimal subtotal =
                ticketLines.Sum(x => x.Subtotal);

            decimal descuentoTotal =
                ticketLines.Sum(x => x.Discount);

            decimal totalFinal =
                ticketLines.Sum(x => x.FinalTotal);

            var certDateGuatemala =
                fel.CertificationDate.Kind == DateTimeKind.Utc
                    ? TimeZoneInfo.ConvertTimeFromUtc(
                        fel.CertificationDate,
                        TimeZoneInfo.FindSystemTimeZoneById(
                            "America/Guatemala"
                        )
                    )
                    : fel.CertificationDate;

            string logoPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Logo INNOVACIONES.jpeg"
            );

            float pageHeight = 260;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(
                        80,
                        pageHeight,
                        Unit.Millimetre
                    );

                    page.MarginHorizontal(
                        3,
                        Unit.Millimetre
                    );

                    page.MarginTop(
                        0,
                        Unit.Millimetre
                    );

                    page.MarginBottom(
                        3,
                        Unit.Millimetre
                    );

                    page.DefaultTextStyle(
                        x => x
                            .FontFamily("Arial")
                            .FontSize(7.8f)
                    );

                    page.Content().Column(col =>
                    {
                        col.Spacing(1);

                        if (File.Exists(logoPath))
                        {
                            col.Item()
                                .AlignCenter()
                                .Width(34, Unit.Millimetre)
                                .Image(logoPath)
                                .FitWidth();
                        }

                        Space(col, 3);

                        col.Item()
                            .AlignCenter()
                            .Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA")
                            .Bold()
                            .FontSize(9.0f);

                        col.Item()
                            .AlignCenter()
                            .Text("INNOVACIONES AGRÍCOLAS DE GUATEMALA, S.A.")
                            .Bold()
                            .FontSize(7.0f);

                        col.Item()
                            .AlignCenter()
                            .Text("NIT: 120074427")
                            .Bold()
                            .FontSize(7.5f);

                        col.Item()
                            .AlignCenter()
                            .Text("CARRETERA INTERAMERICANA, ZONA 0, ALDEA")
                            .FontSize(7.5f);

                        col.Item()
                            .AlignCenter()
                            .Text("TIUCAL, ASUNCIÓN MITA, JUTIAPA")
                            .FontSize(7.0f);

                        Space(col, 5);

                        col.Item()
                            .AlignCenter()
                            .Text("FACTURA")
                            .Bold()
                            .FontSize(10.5f);

                        Space(col, 3);

                        col.Item()
                            .AlignCenter()
                            .Text($"Serie: {fel.Serie}")
                            .Bold()
                            .FontSize(7.5f);

                        col.Item()
                            .AlignCenter()
                            .Text($"Número de DTE: {fel.DteNumber}")
                            .Bold()
                            .FontSize(7.5f);

                        Space(col, 4);

                        col.Item()
                            .AlignCenter()
                            .Text("No. Autorización:")
                            .Bold()
                            .FontSize(8.0f);

                        col.Item()
                            .AlignCenter()
                            .Text(fel.AuthorizationNumber)
                            .Bold()
                            .FontSize(6.8f);

                        Space(col, 3);

                        col.Item()
                            .AlignCenter()
                            .Text(
                                $"Fecha de Certificación: " +
                                $"{certDateGuatemala:dd/MM/yyyy, HH:mm:ss}"
                            )
                            .Bold()
                            .FontSize(6.8f);

                        col.Item()
                            .AlignCenter()
                            .Text($"FECHA DE EMISIÓN: {date}")
                            .Bold()
                            .FontSize(7.5f);

                        Space(col, 3);

                        col.Item().Row(row =>
                        {
                            row.ConstantItem(
                                    15,
                                    Unit.Millimetre
                                )
                                .Text("NIT:")
                                .Bold()
                                .FontSize(7.7f);

                            row.RelativeItem()
                                .Text(customerNit)
                                .FontSize(7.7f);
                        });

                        col.Item()
                            .PaddingTop(1)
                            .Row(row =>
                            {
                                row.ConstantItem(
                                        20,
                                        Unit.Millimetre
                                    )
                                    .Text("NOMBRE:")
                                    .Bold()
                                    .FontSize(7.7f);

                                row.RelativeItem()
                                    .Text(customer.ToUpper())
                                    .FontSize(7.7f);
                            });

                        col.Item()
                            .PaddingTop(1)
                            .Row(row =>
                            {
                                row.ConstantItem(
                                        24,
                                        Unit.Millimetre
                                    )
                                    .Text("TIPO VENTA:")
                                    .Bold()
                                    .FontSize(7.7f);

                                row.RelativeItem()
                                    .Text(tipoVentaTexto)
                                    .FontSize(7.7f);
                            });

                        Space(col, 4);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(
                                    9,
                                    Unit.Millimetre
                                );

                                columns.ConstantColumn(
                                    39,
                                    Unit.Millimetre
                                );

                                columns.ConstantColumn(
                                    10,
                                    Unit.Millimetre
                                );

                                columns.ConstantColumn(
                                    16,
                                    Unit.Millimetre
                                );
                            });

                            table.Header(header =>
                            {
                                header.Cell()
                                    .AlignCenter()
                                    .Text("CANT")
                                    .Bold()
                                    .FontSize(6.7f);

                                header.Cell()
                                    .AlignCenter()
                                    .Text("DETALLE")
                                    .Bold()
                                    .FontSize(6.7f);

                                header.Cell()
                                    .AlignRight()
                                    .Text("Des.")
                                    .Bold()
                                    .FontSize(6.7f);

                                header.Cell()
                                    .AlignRight()
                                    .Text("TOTAL")
                                    .Bold()
                                    .FontSize(6.7f);

                                header.Cell()
                                    .ColumnSpan(4)
                                    .PaddingTop(2)
                                    .LineHorizontal(0.5f);
                            });

                            foreach (var line in ticketLines)
                            {
                                table.Cell()
                                    .PaddingTop(4)
                                    .AlignCenter()
                                    .Text(
                                        line.Quantity.ToString(
                                            "0.##",
                                            CultureInfo.InvariantCulture
                                        )
                                    )
                                    .FontSize(6.4f);

                                table.Cell()
                                    .PaddingTop(4)
                                    .AlignCenter()
                                    .Text(line.Description.ToUpper())
                                    .Bold()
                                    .FontSize(6.0f);

                                table.Cell()
                                    .PaddingTop(4)
                                    .AlignRight()
                                    .Text(line.Discount.ToString("N2"))
                                    .FontSize(6.2f);

                                table.Cell()
                                    .PaddingTop(4)
                                    .AlignRight()
                                    .Text(line.FinalTotal.ToString("N2"))
                                    .Bold()
                                    .FontSize(6.2f);
                            }
                        });

                        /*
                         * Se redujo ligeramente el espacio porque ahora
                         * mostraremos Subtotal, Descuento y Total.
                         */
                        float blankSpaceBeforeTotal =
                            ticketLines.Count switch
                            {
                                <= 1 => 38,
                                2 => 32,
                                3 => 27,
                                4 => 22,
                                5 => 17,
                                6 => 12,
                                _ => 7
                            };

                        Space(col, blankSpaceBeforeTotal);
                        SolidLine(col);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem()
                                .AlignRight()
                                .Text("SUBTOTAL:")
                                .FontSize(7.5f);

                            row.ConstantItem(
                                    22,
                                    Unit.Millimetre
                                )
                                .AlignRight()
                                .Text(
                                    "Q " + subtotal.ToString("N2")
                                )
                                .FontSize(7.5f);
                        });

                        col.Item()
                            .PaddingTop(1)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .AlignRight()
                                    .Text("DESCUENTO:")
                                    .FontSize(7.5f);

                                row.ConstantItem(
                                        22,
                                        Unit.Millimetre
                                    )
                                    .AlignRight()
                                    .Text(
                                        "Q " +
                                        descuentoTotal.ToString("N2")
                                    )
                                    .FontSize(7.5f);
                            });

                        col.Item()
                            .PaddingTop(1)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .AlignRight()
                                    .Text("TOTAL:")
                                    .Bold()
                                    .FontSize(8.0f);

                                row.ConstantItem(
                                        22,
                                        Unit.Millimetre
                                    )
                                    .AlignRight()
                                    .Text(
                                        "Q " +
                                        totalFinal.ToString("N2")
                                    )
                                    .Bold()
                                    .FontSize(8.0f);
                            });

                        Space(col, 4);

                        col.Item()
                            .Text(
                                "TOTAL DE LA FACTURA EN LETRAS:"
                            )
                            .ExtraBold()
                            .FontSize(8.8f);

                        col.Item()
                            .Text(
                                NumberToWords(totalFinal)
                                    .ToUpper()
                            )
                            .FontSize(7.4f);

                        Space(col, 5);

                        col.Item()
                            .AlignCenter()
                            .Text(
                                "SUJETO A PAGOS TRIMESTRALES ISR"
                            )
                            .ExtraBold()
                            .FontSize(8.8f);

                        col.Item()
                            .Text(
                                $"CERTIFICADOR: " +
                                $"{fel.CertifierName}"
                            )
                            .FontSize(7.2f);

                        col.Item()
                            .Text(
                                $"NIT: {fel.CertifierNit}"
                            )
                            .FontSize(7.2f);
                    });
                });
            }).GeneratePdf();
        }

        private static List<TicketLine> ObtenerLineasTicket(
            JsonElement receipt,
            IReadOnlyDictionary<string, decimal> discountMap)
        {
            var result = new List<TicketLine>();

            if (!receipt.TryGetProperty(
                "Line",
                out var lines))
            {
                return result;
            }

            foreach (var line in lines.EnumerateArray())
            {
                if (!line.TryGetProperty(
                    "SalesItemLineDetail",
                    out var detail))
                {
                    continue;
                }

                string lineId =
                    GetString(line, "Id", "");

                decimal quantity =
                    GetDecimal(detail, "Qty", 1);

                if (quantity <= 0)
                {
                    quantity = 1;
                }

                decimal subtotal =
                    GetDecimal(line, "Amount");

                decimal discount = 0;

                if (!string.IsNullOrWhiteSpace(lineId) &&
                    discountMap.TryGetValue(
                        lineId,
                        out var requestedDiscount))
                {
                    discount = requestedDiscount;
                }

                if (discount < 0)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} " +
                        "no puede ser negativo."
                    );
                }

                if (discount > subtotal)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} " +
                        $"no puede superar Q {subtotal:N2}."
                    );
                }

                string description =
                    GetString(line, "Description", "");

                if (string.IsNullOrWhiteSpace(description) &&
                    detail.TryGetProperty(
                        "ItemRef",
                        out var itemRef))
                {
                    description = GetString(
                        itemRef,
                        "name",
                        "Producto"
                    );
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    description = "Producto";
                }

                result.Add(new TicketLine
                {
                    LineId = lineId,
                    Description = description,
                    Quantity = quantity,
                    Subtotal = subtotal,
                    Discount = discount,
                    FinalTotal = subtotal - discount
                });
            }

            return result;
        }

        private static Dictionary<string, decimal>
            CrearMapaDescuentos(
                IReadOnlyCollection<ItemDiscountRequest>? discounts)
        {
            var result =
                new Dictionary<string, decimal>(
                    StringComparer.OrdinalIgnoreCase
                );

            if (discounts == null)
            {
                return result;
            }

            foreach (var discount in discounts)
            {
                if (discount == null)
                {
                    continue;
                }

                string lineId =
                    discount.LineId?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(lineId))
                {
                    throw new Exception(
                        "El descuento no contiene LineId."
                    );
                }

                if (discount.Amount < 0)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} " +
                        "no puede ser negativo."
                    );
                }

                if (result.ContainsKey(lineId))
                {
                    throw new Exception(
                        $"La línea {lineId} está repetida."
                    );
                }

                result[lineId] =
                    Math.Round(
                        discount.Amount,
                        2,
                        MidpointRounding.AwayFromZero
                    );
            }

            return result;
        }

        private static void Space(
            ColumnDescriptor col,
            float millimetres)
        {
            col.Item()
                .Height(
                    millimetres,
                    Unit.Millimetre
                );
        }

        private static void SolidLine(
            ColumnDescriptor col)
        {
            col.Item()
                .LineHorizontal(0.5f);
        }

        private static string GetString(
            JsonElement element,
            string property,
            string fallback = "")
        {
            return element.TryGetProperty(
                property,
                out var value)
                    ? value.GetString() ?? fallback
                    : fallback;
        }

        private static decimal GetDecimal(
            JsonElement element,
            string property,
            decimal fallback = 0)
        {
            if (!element.TryGetProperty(
                property,
                out var value))
            {
                return fallback;
            }

            if (value.ValueKind ==
                    JsonValueKind.Number &&
                value.TryGetDecimal(out var result))
            {
                return result;
            }

            return fallback;
        }

        private static string NumberToWords(
            decimal amount)
        {
            int quetzales =
                (int)Math.Floor(amount);

            int centavos =
                (int)Math.Round(
                    (amount - quetzales) * 100
                );

            if (centavos == 100)
            {
                quetzales++;
                centavos = 0;
            }

            return
                $"{ToWords(quetzales)} " +
                $"QUETZALES CON {centavos:00}/100";
        }

        private static string ToWords(int number)
        {
            if (number == 0)
            {
                return "CERO";
            }

            string[] units =
            {
                "", "UNO", "DOS", "TRES", "CUATRO",
                "CINCO", "SEIS", "SIETE", "OCHO",
                "NUEVE", "DIEZ", "ONCE", "DOCE",
                "TRECE", "CATORCE", "QUINCE",
                "DIECISÉIS", "DIECISIETE",
                "DIECIOCHO", "DIECINUEVE"
            };

            string[] tens =
            {
                "", "", "VEINTE", "TREINTA",
                "CUARENTA", "CINCUENTA", "SESENTA",
                "SETENTA", "OCHENTA", "NOVENTA"
            };

            string[] hundreds =
            {
                "", "CIENTO", "DOSCIENTOS",
                "TRESCIENTOS", "CUATROCIENTOS",
                "QUINIENTOS", "SEISCIENTOS",
                "SETECIENTOS", "OCHOCIENTOS",
                "NOVECIENTOS"
            };

            if (number == 100)
            {
                return "CIEN";
            }

            if (number < 20)
            {
                return units[number];
            }

            if (number < 30)
            {
                return number == 20
                    ? "VEINTE"
                    : "VEINTI" +
                      units[number - 20].ToLower();
            }

            if (number < 100)
            {
                return tens[number / 10] +
                       (
                           number % 10 > 0
                               ? " Y " +
                                 units[number % 10]
                               : ""
                       );
            }

            if (number < 1000)
            {
                return hundreds[number / 100] +
                       (
                           number % 100 > 0
                               ? " " +
                                 ToWords(number % 100)
                               : ""
                       );
            }

            if (number < 1000000)
            {
                int thousands =
                    number / 1000;

                int rest =
                    number % 1000;

                string thousandText =
                    thousands == 1
                        ? "MIL"
                        : ToWords(thousands) + " MIL";

                return thousandText +
                       (
                           rest > 0
                               ? " " + ToWords(rest)
                               : ""
                       );
            }

            return number.ToString();
        }

        private class TicketLine
        {
            public string LineId { get; set; } = "";
            public string Description { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal Subtotal { get; set; }
            public decimal Discount { get; set; }
            public decimal FinalTotal { get; set; }
        }
    }
}