using QBTicketsApi.DTOs;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace QBTicketsApi.Services
{
    public class FelXmlBuilderService
    {
        private readonly CustomerLookupService _customerLookupService;

        public FelXmlBuilderService(
            CustomerLookupService customerLookupService)
        {
            _customerLookupService = customerLookupService;
        }

        public string BuildFactXml(
            string quickBooksJson,
            string? nitOverride = null,
            string? customerNameOverride = null,
            IReadOnlyCollection<ItemDiscountRequest>? discounts = null)
        {
            using JsonDocument doc =
                JsonDocument.Parse(quickBooksJson);

            JsonElement query =
                doc.RootElement.GetProperty(
                    "QueryResponse"
                );

            JsonElement qbDoc;

            if (query.TryGetProperty(
                "Invoice",
                out JsonElement invoices))
            {
                qbDoc = invoices[0];
            }
            else if (query.TryGetProperty(
                "SalesReceipt",
                out JsonElement receipts))
            {
                qbDoc = receipts[0];
            }
            else
            {
                throw new Exception(
                    "No se encontró Invoice ni SalesReceipt."
                );
            }

            string date =
                GetString(
                    qbDoc,
                    "TxnDate",
                    DateTime.Now.ToString("yyyy-MM-dd")
                );

            decimal totalOriginal =
                GetDecimal(
                    qbDoc,
                    "TotalAmt"
                );

            Dictionary<string, decimal> discountMap =
                CrearMapaDescuentos(
                    discounts
                );

            ValidarDescuentosPorLinea(
                qbDoc,
                discountMap
            );

            decimal descuentoTotal =
                discountMap.Values.Sum();

            if (descuentoTotal > totalOriginal)
            {
                throw new Exception(
                    "El descuento total no puede superar " +
                    "el total del documento."
                );
            }

            decimal totalFinal =
                totalOriginal - descuentoTotal;

            string customerName =
                ObtenerNombreCliente(qbDoc);

            if (!string.IsNullOrWhiteSpace(
                customerNameOverride))
            {
                customerName =
                    customerNameOverride.Trim();
            }

            string customerNit =
                !string.IsNullOrWhiteSpace(nitOverride)
                    ? nitOverride
                        .Trim()
                        .Replace("-", "")
                    : _customerLookupService
                        .GetNit(customerName);

            if (string.IsNullOrWhiteSpace(customerNit))
            {
                customerNit = "CF";
            }

            if (customerNit.Equals(
                "CF",
                StringComparison.OrdinalIgnoreCase))
            {
                customerName =
                    "Consumidor Final";
            }

            decimal montoGravableTotal =
                Math.Round(
                    totalFinal / 1.12m,
                    6,
                    MidpointRounding.AwayFromZero
                );

            decimal ivaTotal =
                Math.Round(
                    totalFinal - montoGravableTotal,
                    6,
                    MidpointRounding.AwayFromZero
                );

            XNamespace dte =
                "http://www.sat.gob.gt/dte/fel/0.2.0";

            XNamespace xsi =
                "http://www.w3.org/2001/XMLSchema-instance";

            string fechaHoraEmision =
                BuildFechaHoraEmision(date);

            var xml =
                new XDocument(
                    new XDeclaration(
                        "1.0",
                        "UTF-8",
                        "no"
                    ),

                    new XElement(
                        dte + "GTDocumento",

                        new XAttribute(
                            "Version",
                            "0.1"
                        ),

                        new XAttribute(
                            XNamespace.Xmlns + "dte",
                            dte
                        ),

                        new XAttribute(
                            XNamespace.Xmlns + "xsi",
                            xsi
                        ),

                        new XElement(
                            dte + "SAT",

                            new XAttribute(
                                "ClaseDocumento",
                                "dte"
                            ),

                            new XElement(
                                dte + "DTE",

                                new XAttribute(
                                    "ID",
                                    "DatosCertificados"
                                ),

                                new XElement(
                                    dte + "DatosEmision",

                                    new XAttribute(
                                        "ID",
                                        "DatosEmision"
                                    ),

                                    new XElement(
                                        dte + "DatosGenerales",

                                        new XAttribute(
                                            "CodigoMoneda",
                                            "GTQ"
                                        ),

                                        new XAttribute(
                                            "FechaHoraEmision",
                                            fechaHoraEmision
                                        ),

                                        new XAttribute(
                                            "Tipo",
                                            "FACT"
                                        )
                                    ),

                                    new XElement(
                                        dte + "Emisor",

                                        new XAttribute(
                                            "AfiliacionIVA",
                                            "GEN"
                                        ),

                                        new XAttribute(
                                            "CodigoEstablecimiento",
                                            "1"
                                        ),

                                        new XAttribute(
                                            "CorreoEmisor",
                                            ""
                                        ),

                                        new XAttribute(
                                            "NITEmisor",
                                            "120074427"
                                        ),

                                        new XAttribute(
                                            "NombreComercial",
                                            "INNOVACIONES AGRÍCOLAS DE GUATEMALA"
                                        ),

                                        new XAttribute(
                                            "NombreEmisor",
                                            "INNOVACIONES AGRÍCOLAS DE GUATEMALA, SOCIEDAD ANÓNIMA"
                                        ),

                                        new XElement(
                                            dte + "DireccionEmisor",

                                            new XElement(
                                            dte + "Direccion",
                                            "CARRETERA INTERAMERICANA, ZONA 0, ALDEA TIUCAL"
                                           ),

                                            new XElement(
                                                dte + "CodigoPostal",
                                                "22005"
                                            ),

                                            new XElement(
                                                dte + "Municipio",
                                                "ASUNCIÓN MITA"
                                            ),

                                            new XElement(
                                                dte + "Departamento",
                                                "JUTIAPA"
                                            ),

                                            new XElement(
                                                dte + "Pais",
                                                "GT"
                                            )
                                        )
                                    ),

                                    new XElement(
                                        dte + "Receptor",

                                        new XAttribute(
                                            "CorreoReceptor",
                                            ""
                                        ),

                                        new XAttribute(
                                            "IDReceptor",
                                            customerNit
                                        ),

                                        new XAttribute(
                                            "NombreReceptor",
                                            customerName
                                        ),

                                        new XElement(
                                            dte + "DireccionReceptor",

                                            new XElement(
                                                dte + "Direccion",
                                                "CIUDAD"
                                            ),

                                            new XElement(
                                                dte + "CodigoPostal",
                                                "01001"
                                            ),

                                            new XElement(
                                                dte + "Municipio",
                                                "GUATEMALA"
                                            ),

                                            new XElement(
                                                dte + "Departamento",
                                                "GUATEMALA"
                                            ),

                                            new XElement(
                                                dte + "Pais",
                                                "GT"
                                            )
                                        )
                                    ),

                                    new XElement(
                                        dte + "Frases",

                                        new XElement(
                                            dte + "Frase",

                                            new XAttribute(
                                                "CodigoEscenario",
                                                "1"
                                            ),

                                            new XAttribute(
                                                "TipoFrase",
                                                "1"
                                            )
                                        )
                                    ),

                                    BuildItems(
                                        qbDoc,
                                        dte,
                                        discountMap
                                    ),

                                    new XElement(
                                        dte + "Totales",

                                        new XElement(
                                            dte + "TotalImpuestos",

                                            new XElement(
                                                dte + "TotalImpuesto",

                                                new XAttribute(
                                                    "NombreCorto",
                                                    "IVA"
                                                ),

                                                new XAttribute(
                                                    "TotalMontoImpuesto",
                                                    FormatoDecimal(
                                                        ivaTotal
                                                    )
                                                )
                                            )
                                        ),

                                        new XElement(
                                            dte + "GranTotal",
                                            FormatoDecimal(
                                                totalFinal
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    )
                );

            return xml.ToString(
                SaveOptions.DisableFormatting
            );
        }

        private XElement BuildItems(
            JsonElement qbDoc,
            XNamespace dte,
            IReadOnlyDictionary<string, decimal> discountMap)
        {
            var items =
                new XElement(
                    dte + "Items"
                );

            int lineNumber = 1;

            if (!qbDoc.TryGetProperty(
                "Line",
                out JsonElement lines))
            {
                throw new Exception(
                    "El documento no contiene líneas."
                );
            }

            foreach (JsonElement line in lines.EnumerateArray())
            {
                if (!line.TryGetProperty(
                    "SalesItemLineDetail",
                    out JsonElement detail))
                {
                    continue;
                }

                string lineId =
                    GetString(
                        line,
                        "Id",
                        ""
                    );

                decimal qty =
                    GetDecimal(
                        detail,
                        "Qty",
                        1
                    );

                if (qty <= 0)
                {
                    qty = 1;
                }

                decimal amountOriginal =
                    GetDecimal(
                        line,
                        "Amount"
                    );

                decimal descuentoLinea = 0;

                if (!string.IsNullOrWhiteSpace(lineId) &&
                    discountMap.TryGetValue(
                        lineId,
                        out decimal requestedDiscount))
                {
                    descuentoLinea =
                        requestedDiscount;
                }

                if (descuentoLinea < 0)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} " +
                        "no puede ser negativo."
                    );
                }

                if (descuentoLinea > amountOriginal)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} " +
                        "no puede superar Q " +
                        $"{amountOriginal:N2}."
                    );
                }

                decimal amountFinal =
                    amountOriginal -
                    descuentoLinea;

                /*
                 * Se toma primero el precio unitario
                 * enviado por QuickBooks.
                 */
                decimal price =
                    GetDecimal(
                        detail,
                        "UnitPrice",
                        0
                    );

                /*
                 * Si QuickBooks no devuelve UnitPrice,
                 * se calcula usando total / cantidad.
                 */
                if (price <= 0 && qty > 0)
                {
                    price =
                        amountOriginal / qty;
                }

                /*
                 * Se toma primero el nombre real del artículo:
                 * SalesItemLineDetail -> ItemRef -> name.
                 */
                string description = "";

                if (detail.TryGetProperty(
                    "ItemRef",
                    out JsonElement itemRef))
                {
                    description =
                        GetString(
                            itemRef,
                            "name",
                            ""
                        );
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    description =
                        GetString(
                            line,
                            "Description",
                            ""
                        );
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    description = "Producto";
                }

                decimal taxable =
                    Math.Round(
                        amountFinal / 1.12m,
                        6,
                        MidpointRounding.AwayFromZero
                    );

                decimal tax =
                    Math.Round(
                        amountFinal - taxable,
                        6,
                        MidpointRounding.AwayFromZero
                    );

                items.Add(
                    new XElement(
                        dte + "Item",

                        new XAttribute(
                            "BienOServicio",
                            "B"
                        ),

                        new XAttribute(
                            "NumeroLinea",
                            lineNumber
                        ),

                        new XElement(
                            dte + "Cantidad",
                            qty.ToString(
                                "0.######",
                                CultureInfo.InvariantCulture
                            )
                        ),

                        new XElement(
                            dte + "UnidadMedida",
                            "UNI"
                        ),

                        new XElement(
                            dte + "Descripcion",
                            description.Trim()
                        ),

                        new XElement(
                            dte + "PrecioUnitario",
                            FormatoDecimal(
                                price
                            )
                        ),

                        new XElement(
                            dte + "Precio",
                            FormatoDecimal(
                                amountOriginal
                            )
                        ),

                        new XElement(
                            dte + "Descuento",
                            FormatoDecimal(
                                descuentoLinea
                            )
                        ),

                        new XElement(
                            dte + "Impuestos",

                            new XElement(
                                dte + "Impuesto",

                                new XElement(
                                    dte + "NombreCorto",
                                    "IVA"
                                ),

                                new XElement(
                                    dte + "CodigoUnidadGravable",
                                    "1"
                                ),

                                new XElement(
                                    dte + "MontoGravable",
                                    FormatoDecimal(
                                        taxable
                                    )
                                ),

                                new XElement(
                                    dte + "MontoImpuesto",
                                    FormatoDecimal(
                                        tax
                                    )
                                )
                            )
                        ),

                        new XElement(
                            dte + "Total",
                            FormatoDecimal(
                                amountFinal
                            )
                        )

                    )

                );

                lineNumber++;
            }

            if (!items.HasElements)
            {
                throw new Exception(
                    "El documento no contiene productos válidos."
                );
            }

            return items;
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

            foreach (ItemDiscountRequest discount in discounts)
            {
                if (discount == null)
                {
                    continue;
                }

                string lineId =
                    discount.LineId?.Trim()
                    ?? "";

                if (string.IsNullOrWhiteSpace(lineId))
                {
                    throw new Exception(
                        "Todo descuento debe tener un LineId."
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
                        $"La línea {lineId} tiene el descuento " +
                        "repetido."
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

        private static void ValidarDescuentosPorLinea(
            JsonElement qbDoc,
            IReadOnlyDictionary<string, decimal> discountMap)
        {
            if (discountMap.Count == 0)
            {
                return;
            }

            var availableLines =
                new Dictionary<string, decimal>(
                    StringComparer.OrdinalIgnoreCase
                );

            if (!qbDoc.TryGetProperty(
                "Line",
                out JsonElement lines))
            {
                throw new Exception(
                    "El documento no contiene líneas."
                );
            }

            foreach (JsonElement line in lines.EnumerateArray())
            {
                if (!line.TryGetProperty(
                    "SalesItemLineDetail",
                    out _))
                {
                    continue;
                }

                string lineId =
                    GetString(
                        line,
                        "Id",
                        ""
                    );

                if (string.IsNullOrWhiteSpace(lineId))
                {
                    continue;
                }

                availableLines[lineId] =
                    GetDecimal(
                        line,
                        "Amount"
                    );
            }

            foreach (
                KeyValuePair<string, decimal> discount
                in discountMap)
            {
                if (!availableLines.TryGetValue(
                    discount.Key,
                    out decimal lineAmount))
                {
                    throw new Exception(
                        $"No se encontró la línea " +
                        $"{discount.Key} en QuickBooks."
                    );
                }

                if (discount.Value > lineAmount)
                {
                    throw new Exception(
                        $"El descuento de la línea " +
                        $"{discount.Key} no puede superar " +
                        $"Q {lineAmount:N2}."
                    );
                }
            }
        }

        private static string ObtenerNombreCliente(
            JsonElement qbDoc)
        {
            if (qbDoc.TryGetProperty(
                "CustomerRef",
                out JsonElement customerRef))
            {
                return GetString(
                    customerRef,
                    "name",
                    "Consumidor Final"
                );
            }

            return "Consumidor Final";
        }

        private static string GetString(
            JsonElement element,
            string property,
            string fallback = "")
        {
            return element.TryGetProperty(
                property,
                out JsonElement value)
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
                out JsonElement value))
            {
                return fallback;
            }

            return value.TryGetDecimal(
                out decimal result)
                    ? result
                    : fallback;
        }

        private static string FormatoDecimal(
            decimal value)
        {
            return value.ToString(
                "0.000000",
                CultureInfo.InvariantCulture
            );
        }

        private static string BuildFechaHoraEmision(
            string txnDate)
        {
            TimeSpan guatemalaOffset =
                TimeSpan.FromHours(-6);

            DateTime baseDate =
                DateTime.TryParse(
                    txnDate,
                    out DateTime parsed)
                    ? parsed.Date
                    : DateTime.UtcNow.Date;

            DateTimeOffset nowGuatemala =
                new DateTimeOffset(
                    DateTime.UtcNow,
                    TimeSpan.Zero
                )
                .ToOffset(
                    guatemalaOffset
                );

            var emision =
                new DateTimeOffset(
                    baseDate.Year,
                    baseDate.Month,
                    baseDate.Day,
                    nowGuatemala.Hour,
                    nowGuatemala.Minute,
                    nowGuatemala.Second,
                    guatemalaOffset
                );

            return emision.ToString(
                "yyyy-MM-ddTHH:mm:sszzz"
            );
        }
    }
}