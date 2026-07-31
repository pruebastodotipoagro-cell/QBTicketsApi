using QBTicketsApi.DTOs;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace QBTicketsApi.Services
{
    public class FelXmlBuilderService
    {
        private readonly CustomerLookupService _customerLookupService;

        public FelXmlBuilderService(CustomerLookupService customerLookupService)
        {
            _customerLookupService = customerLookupService;
        }

        public string BuildFactXml(
            string quickBooksJson,
            string? nitOverride = null,
            string? customerNameOverride = null,
            IReadOnlyCollection<ItemDiscountRequest>? discounts = null,
            string priceType = "contado",
            decimal creditPercentage = 0m)
        {
            using JsonDocument doc = JsonDocument.Parse(quickBooksJson);
            JsonElement query = doc.RootElement.GetProperty("QueryResponse");

            JsonElement qbDoc;
            if (query.TryGetProperty("Invoice", out JsonElement invoices))
                qbDoc = invoices[0];
            else if (query.TryGetProperty("SalesReceipt", out JsonElement receipts))
                qbDoc = receipts[0];
            else
                throw new Exception("No se encontró Invoice ni SalesReceipt.");

            string normalizedPriceType = NormalizarTipoPrecio(priceType);
            decimal normalizedCreditPercentage = NormalizarPorcentajeCredito(normalizedPriceType, creditPercentage);
            /*
             * Los precios ya fueron sincronizados en QuickBooks antes
             * de construir el XML. No se vuelve a aplicar el 3 % aquí.
             */
            decimal priceFactor = 1m;

            string date = GetString(qbDoc, "TxnDate", DateTime.Now.ToString("yyyy-MM-dd"));
            Dictionary<string, decimal> discountMap = CrearMapaDescuentos(discounts);
            List<FelLineData> lineas = ObtenerLineas(qbDoc, discountMap, priceFactor);

            decimal subtotal = lineas.Sum(x => x.Subtotal);
            decimal descuentoTotal = lineas.Sum(x => x.Discount);
            decimal totalFinal = lineas.Sum(x => x.FinalTotal);

            if (descuentoTotal > subtotal)
                throw new Exception("El descuento total no puede superar el subtotal del documento.");

            string customerName = ObtenerNombreCliente(qbDoc);
            if (!string.IsNullOrWhiteSpace(customerNameOverride))
                customerName = customerNameOverride.Trim();

            string customerNit = !string.IsNullOrWhiteSpace(nitOverride)
                ? nitOverride.Trim().Replace("-", "").Replace(" ", "")
                : _customerLookupService.GetNit(customerName);

            if (string.IsNullOrWhiteSpace(customerNit)) customerNit = "CF";
            if (string.IsNullOrWhiteSpace(customerName)) customerName = "Consumidor Final";

            decimal montoGravableTotal = Math.Round(totalFinal / 1.12m, 6, MidpointRounding.AwayFromZero);
            decimal ivaTotal = Math.Round(totalFinal - montoGravableTotal, 6, MidpointRounding.AwayFromZero);

            XNamespace dte = "http://www.sat.gob.gt/dte/fel/0.2.0";
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            string fechaHoraEmision = BuildFechaHoraEmision(date);

            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", "no"),
                new XElement(dte + "GTDocumento",
                    new XAttribute("Version", "0.1"),
                    new XAttribute(XNamespace.Xmlns + "dte", dte),
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XElement(dte + "SAT",
                        new XAttribute("ClaseDocumento", "dte"),
                        new XElement(dte + "DTE",
                            new XAttribute("ID", "DatosCertificados"),
                            new XElement(dte + "DatosEmision",
                                new XAttribute("ID", "DatosEmision"),
                                new XElement(dte + "DatosGenerales",
                                    new XAttribute("CodigoMoneda", "GTQ"),
                                    new XAttribute("FechaHoraEmision", fechaHoraEmision),
                                    new XAttribute("Tipo", "FACT")),
                                new XElement(dte + "Emisor",
                                    new XAttribute("AfiliacionIVA", "GEN"),
                                    new XAttribute("CodigoEstablecimiento", "1"),
                                    new XAttribute("CorreoEmisor", ""),
                                    new XAttribute("NITEmisor", "120074427"),
                                    new XAttribute("NombreComercial", "INNOVACIONES AGRÍCOLAS DE GUATEMALA"),
                                    new XAttribute("NombreEmisor", "INNOVACIONES AGRÍCOLAS DE GUATEMALA, SOCIEDAD ANÓNIMA"),
                                    new XElement(dte + "DireccionEmisor",
                                        new XElement(dte + "Direccion", "CARRETERA INTERAMERICANA, ZONA 0, ALDEA TIUCAL"),
                                        new XElement(dte + "CodigoPostal", "22005"),
                                        new XElement(dte + "Municipio", "ASUNCIÓN MITA"),
                                        new XElement(dte + "Departamento", "JUTIAPA"),
                                        new XElement(dte + "Pais", "GT"))),
                                new XElement(dte + "Receptor",
                                    new XAttribute("CorreoReceptor", ""),
                                    new XAttribute("IDReceptor", customerNit),
                                    new XAttribute("NombreReceptor", customerName),
                                    new XElement(dte + "DireccionReceptor",
                                        new XElement(dte + "Direccion", "CIUDAD"),
                                        new XElement(dte + "CodigoPostal", "01001"),
                                        new XElement(dte + "Municipio", "GUATEMALA"),
                                        new XElement(dte + "Departamento", "GUATEMALA"),
                                        new XElement(dte + "Pais", "GT"))),
                                new XElement(dte + "Frases",
                                    new XElement(dte + "Frase",
                                        new XAttribute("CodigoEscenario", "1"),
                                        new XAttribute("TipoFrase", "1"))),
                                BuildItems(lineas, dte),
                                new XElement(dte + "Totales",
                                    new XElement(dte + "TotalImpuestos",
                                        new XElement(dte + "TotalImpuesto",
                                            new XAttribute("NombreCorto", "IVA"),
                                            new XAttribute("TotalMontoImpuesto", FormatoDecimal(ivaTotal)))),
                                    new XElement(dte + "GranTotal", FormatoDecimal(totalFinal))))))));

            return xml.ToString(SaveOptions.DisableFormatting);
        }

        private static XElement BuildItems(IReadOnlyCollection<FelLineData> lineas, XNamespace dte)
        {
            var items = new XElement(dte + "Items");
            int lineNumber = 1;

            foreach (FelLineData line in lineas)
            {
                decimal taxable = Math.Round(line.FinalTotal / 1.12m, 6, MidpointRounding.AwayFromZero);
                decimal tax = Math.Round(line.FinalTotal - taxable, 6, MidpointRounding.AwayFromZero);

                items.Add(new XElement(dte + "Item",
                    new XAttribute("BienOServicio", "B"),
                    new XAttribute("NumeroLinea", lineNumber),
                    new XElement(dte + "Cantidad", line.Quantity.ToString("0.######", CultureInfo.InvariantCulture)),
                    new XElement(dte + "UnidadMedida", "UNI"),
                    new XElement(dte + "Descripcion", line.Description),
                    new XElement(dte + "PrecioUnitario", FormatoDecimal(line.UnitPrice)),
                    new XElement(dte + "Precio", FormatoDecimal(line.Subtotal)),
                    new XElement(dte + "Descuento", FormatoDecimal(line.Discount)),
                    new XElement(dte + "Impuestos",
                        new XElement(dte + "Impuesto",
                            new XElement(dte + "NombreCorto", "IVA"),
                            new XElement(dte + "CodigoUnidadGravable", "1"),
                            new XElement(dte + "MontoGravable", FormatoDecimal(taxable)),
                            new XElement(dte + "MontoImpuesto", FormatoDecimal(tax)))),
                    new XElement(dte + "Total", FormatoDecimal(line.FinalTotal))));

                lineNumber++;
            }

            if (!items.HasElements)
                throw new Exception("El documento no contiene productos válidos.");

            return items;
        }

        private static List<FelLineData> ObtenerLineas(
            JsonElement qbDoc,
            IReadOnlyDictionary<string, decimal> discountMap,
            decimal priceFactor)
        {
            var result = new List<FelLineData>();
            if (!qbDoc.TryGetProperty("Line", out JsonElement lines))
                throw new Exception("El documento no contiene líneas.");

            var availableLineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonElement line in lines.EnumerateArray())
            {
                if (!line.TryGetProperty("SalesItemLineDetail", out JsonElement detail))
                    continue;

                string lineId = GetString(line, "Id", "");
                if (!string.IsNullOrWhiteSpace(lineId)) availableLineIds.Add(lineId);

                decimal quantity = GetDecimal(detail, "Qty", 1m);
                if (quantity <= 0m) quantity = 1m;

                decimal originalSubtotal = GetDecimal(line, "Amount");
                decimal originalUnitPrice = GetDecimal(detail, "UnitPrice", 0m);
                if (originalUnitPrice <= 0m) originalUnitPrice = originalSubtotal / quantity;

                decimal appliedUnitPrice = Math.Round(originalUnitPrice * priceFactor, 6, MidpointRounding.AwayFromZero);
                decimal appliedSubtotal = Math.Round(appliedUnitPrice * quantity, 2, MidpointRounding.AwayFromZero);

                decimal discount = 0m;
                if (!string.IsNullOrWhiteSpace(lineId) && discountMap.TryGetValue(lineId, out decimal requestedDiscount))
                    discount = requestedDiscount;

                if (discount < 0m)
                    throw new Exception($"El descuento de la línea {lineId} no puede ser negativo.");

                if (discount > appliedSubtotal)
                    throw new Exception($"El descuento de la línea {lineId} no puede superar Q {appliedSubtotal:N2}.");

                string description = "";
                if (detail.TryGetProperty("ItemRef", out JsonElement itemRef))
                    description = GetString(itemRef, "name", "");
                if (string.IsNullOrWhiteSpace(description))
                    description = GetString(line, "Description", "");
                if (string.IsNullOrWhiteSpace(description))
                    description = "Producto";

                result.Add(new FelLineData
                {
                    LineId = lineId,
                    Description = description.Trim(),
                    Quantity = quantity,
                    UnitPrice = appliedUnitPrice,
                    Subtotal = appliedSubtotal,
                    Discount = discount,
                    FinalTotal = appliedSubtotal - discount
                });
            }

            foreach (KeyValuePair<string, decimal> discount in discountMap)
            {
                if (!availableLineIds.Contains(discount.Key))
                    throw new Exception($"No se encontró la línea {discount.Key} en QuickBooks.");
            }

            if (result.Count == 0)
                throw new Exception("El documento no contiene productos válidos.");

            return result;
        }

        private static string NormalizarTipoPrecio(string? priceType)
        {
            string normalized = (priceType ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace("é", "e")
                .Replace("í", "i");

            if (string.IsNullOrWhiteSpace(normalized)) normalized = "contado";

            if (normalized != "contado" && normalized != "credito")
                throw new Exception("El tipo de precio debe ser contado o crédito.");

            return normalized;
        }

        private static decimal NormalizarPorcentajeCredito(string priceType, decimal creditPercentage)
        {
            if (priceType == "contado") return 0m;
            if (creditPercentage <= 0m) return 3m;
            if (creditPercentage != 3m)
                throw new Exception("El porcentaje del precio crédito debe ser 3%.");
            return creditPercentage;
        }

        private static Dictionary<string, decimal> CrearMapaDescuentos(
            IReadOnlyCollection<ItemDiscountRequest>? discounts)
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (discounts == null) return result;

            foreach (ItemDiscountRequest discount in discounts)
            {
                if (discount == null) continue;
                string lineId = discount.LineId?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(lineId))
                    throw new Exception("Todo descuento debe tener un LineId.");
                if (discount.Amount < 0m)
                    throw new Exception($"El descuento de la línea {lineId} no puede ser negativo.");
                if (result.ContainsKey(lineId))
                    throw new Exception($"La línea {lineId} tiene el descuento repetido.");

                result[lineId] = Math.Round(discount.Amount, 2, MidpointRounding.AwayFromZero);
            }

            return result;
        }

        private static string ObtenerNombreCliente(JsonElement qbDoc)
        {
            if (qbDoc.TryGetProperty("CustomerRef", out JsonElement customerRef))
                return GetString(customerRef, "name", "Consumidor Final");
            return "Consumidor Final";
        }

        private static string GetString(JsonElement element, string property, string fallback = "")
        {
            if (!element.TryGetProperty(property, out JsonElement value)) return fallback;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? fallback;
            return value.ToString();
        }

        private static decimal GetDecimal(JsonElement element, string property, decimal fallback = 0m)
        {
            if (!element.TryGetProperty(property, out JsonElement value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal result)) return result;
            return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed)
                ? parsed
                : fallback;
        }

        private static string FormatoDecimal(decimal value)
        {
            return value.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        private static string BuildFechaHoraEmision(string txnDate)
        {
            TimeSpan guatemalaOffset = TimeSpan.FromHours(-6);
            DateTime baseDate = DateTime.TryParse(txnDate, out DateTime parsed)
                ? parsed.Date
                : DateTime.UtcNow.Date;

            DateTimeOffset nowGuatemala = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero)
                .ToOffset(guatemalaOffset);

            var emision = new DateTimeOffset(
                baseDate.Year,
                baseDate.Month,
                baseDate.Day,
                nowGuatemala.Hour,
                nowGuatemala.Minute,
                nowGuatemala.Second,
                guatemalaOffset);

            return emision.ToString("yyyy-MM-ddTHH:mm:sszzz");
        }

        private sealed class FelLineData
        {
            public string LineId { get; set; } = "";
            public string Description { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal Subtotal { get; set; }
            public decimal Discount { get; set; }
            public decimal FinalTotal { get; set; }
        }
    }
}