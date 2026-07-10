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

        public string BuildFactXml(string quickBooksJson, string? nitOverride = null, decimal descuentoTotal = 0)
        {
            using var doc = JsonDocument.Parse(quickBooksJson);
            var query = doc.RootElement.GetProperty("QueryResponse");

            JsonElement qbDoc;

            if (query.TryGetProperty("Invoice", out var invoices))
                qbDoc = invoices[0];
            else if (query.TryGetProperty("SalesReceipt", out var receipts))
                qbDoc = receipts[0];
            else
                throw new Exception("No se encontró Invoice ni SalesReceipt.");

            string docNumber = GetString(qbDoc, "DocNumber", "SIN-NUMERO");
            string date = GetString(qbDoc, "TxnDate", DateTime.Now.ToString("yyyy-MM-dd"));
            decimal totalOriginal = GetDecimal(qbDoc, "TotalAmt");

            if (descuentoTotal < 0)
                descuentoTotal = 0;

            if (descuentoTotal > totalOriginal)
                descuentoTotal = totalOriginal;

            decimal total = totalOriginal - descuentoTotal;

            string customerName = "Consumidor Final";

            if (qbDoc.TryGetProperty("CustomerRef", out var customerRef))
                customerName = GetString(customerRef, "name", "Consumidor Final");

            string customerNit = !string.IsNullOrWhiteSpace(nitOverride)
                ? nitOverride
                : _customerLookupService.GetNit(customerName);
            if (string.IsNullOrWhiteSpace(customerNit)) customerNit = "CF";

            decimal montoGravable = Math.Round(total / 1.12m, 6);
            decimal iva = Math.Round(total - montoGravable, 6);

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
                                    new XAttribute("Tipo", "FACT")
                                ),

                                new XElement(dte + "Emisor",
                                    new XAttribute("AfiliacionIVA", "GEN"),
                                    new XAttribute("CodigoEstablecimiento", "1"),
                                    new XAttribute("CorreoEmisor", ""),
                                    new XAttribute("NITEmisor", "120074427"),
                                    new XAttribute("NombreComercial", "INNOVACIONES AGRICOLAS"),
                                    new XAttribute("NombreEmisor", "INNOVACIONES AGRICOLAS DE GUATEMALA, SOCIEDAD ANONIMA"),

                                    new XElement(dte + "DireccionEmisor",
                                        new XElement(dte + "Direccion", "ALDEA TIUCAL"),
                                        new XElement(dte + "CodigoPostal", "22005"),
                                        new XElement(dte + "Municipio", "ASUNCION MITA"),
                                        new XElement(dte + "Departamento", "JUTIAPA"),
                                        new XElement(dte + "Pais", "GT")
                                    )
                                ),

                                new XElement(dte + "Receptor",
                                    new XAttribute("CorreoReceptor", ""),
                                    new XAttribute("IDReceptor", customerNit),
                                    new XAttribute("NombreReceptor", customerName),

                                    new XElement(dte + "DireccionReceptor",
                                        new XElement(dte + "Direccion", "CIUDAD"),
                                        new XElement(dte + "CodigoPostal", "01001"),
                                        new XElement(dte + "Municipio", "GUATEMALA"),
                                        new XElement(dte + "Departamento", "GUATEMALA"),
                                        new XElement(dte + "Pais", "GT")
                                    )
                                ),

                                new XElement(dte + "Frases",
                                    new XElement(dte + "Frase",
                                        // Confirmado por Megaprint (caso #131661): régimen ISR sobre utilidades de actividades lucrativas
                                        new XAttribute("CodigoEscenario", "1"),
                                        new XAttribute("TipoFrase", "1")
                                    )
                                ),

                                BuildItems(qbDoc, dte, descuentoTotal, totalOriginal),

                                new XElement(dte + "Totales",
                                    new XElement(dte + "TotalImpuestos",
                                        new XElement(dte + "TotalImpuesto",
                                            new XAttribute("NombreCorto", "IVA"),
                                            new XAttribute("TotalMontoImpuesto", iva.ToString("0.000000"))
                                        )
                                    ),
                                    new XElement(dte + "GranTotal", total.ToString("0.000000"))
                                )
                            )
                        )
                    )
                )
            );

            return xml.ToString(SaveOptions.DisableFormatting);
        }

        private XElement BuildItems(JsonElement qbDoc, XNamespace dte, decimal descuentoTotal, decimal totalOriginal)
        {
            var items = new XElement(dte + "Items");

            int lineNumber = 1;

            foreach (var line in qbDoc.GetProperty("Line").EnumerateArray())
            {
                if (!line.TryGetProperty("SalesItemLineDetail", out var detail))
                    continue;

                decimal qty = GetDecimal(detail, "Qty", 1);
                decimal amountOriginal = GetDecimal(line, "Amount");

                decimal descuentoLinea = 0;

                if (descuentoTotal > 0 && totalOriginal > 0)
                {
                    descuentoLinea = Math.Round((amountOriginal / totalOriginal) * descuentoTotal, 6);
                }

                decimal amountFinal = amountOriginal - descuentoLinea;

                if (amountFinal < 0)
                    amountFinal = 0;

                decimal price = qty > 0 ? amountOriginal / qty : amountOriginal;

                string desc = "Producto";
                if (detail.TryGetProperty("ItemRef", out var itemRef))
                    desc = GetString(itemRef, "name", "Producto");

                decimal taxable = Math.Round(amountFinal / 1.12m, 6);
                decimal tax = Math.Round(amountFinal - taxable, 6);

                items.Add(
                    new XElement(dte + "Item",
                        new XAttribute("BienOServicio", "B"),
                        new XAttribute("NumeroLinea", lineNumber),

                        new XElement(dte + "Cantidad", qty.ToString("0.######")),
                        new XElement(dte + "UnidadMedida", "UNI"),
                        new XElement(dte + "Descripcion", desc),
                        new XElement(dte + "PrecioUnitario", price.ToString("0.000000")),
                        new XElement(dte + "Precio", amountOriginal.ToString("0.000000")),
                        new XElement(dte + "Descuento", descuentoLinea.ToString("0.000000")),

                        new XElement(dte + "Impuestos",
                            new XElement(dte + "Impuesto",
                                new XElement(dte + "NombreCorto", "IVA"),
                                new XElement(dte + "CodigoUnidadGravable", "1"),
                                new XElement(dte + "MontoGravable", taxable.ToString("0.000000")),
                                new XElement(dte + "MontoImpuesto", tax.ToString("0.000000"))
                            )
                        ),

                        new XElement(dte + "Total", amountFinal.ToString("0.000000"))
                    )
                );

                lineNumber++;
            }

            return items;
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
            return value.TryGetDecimal(out var result) ? result : fallback;
        }

        private static string BuildFechaHoraEmision(string txnDate)
        {
            var guatemalaOffset = TimeSpan.FromHours(-6);

            DateTime baseDate = DateTime.TryParse(txnDate, out var parsed)
                ? parsed.Date
                : DateTime.UtcNow.Date;

            var nowGuatemala = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero)
                .ToOffset(guatemalaOffset);

            var emision = new DateTimeOffset(
                baseDate.Year, baseDate.Month, baseDate.Day,
                nowGuatemala.Hour, nowGuatemala.Minute, nowGuatemala.Second,
                guatemalaOffset);

            return emision.ToString("yyyy-MM-ddTHH:mm:sszzz");
        }
    }
}