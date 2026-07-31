using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.Models;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace QBTicketsApi.Services
{
    public class FelResult
    {
        public string Serie { get; set; } = "";

        public string DteNumber { get; set; } = "";

        public string AuthorizationNumber { get; set; } = "";

        public DateTime CertificationDate { get; set; }

        public string Qr { get; set; } = "";

        public string CustomerNit { get; set; } = "";

        public string CustomerName { get; set; } = "";

        public string CertifierName { get; set; } = "";

        public string CertifierNit { get; set; } = "";
    }

    public class FelService
    {
        private readonly AppDbContext _db;

        private readonly MegaprintService _megaprintService;

        private readonly FelXmlBuilderService _xmlBuilder;

        private readonly CustomerLookupService _customerLookupService;

        public FelService(
            AppDbContext db,
            MegaprintService megaprintService,
            FelXmlBuilderService xmlBuilder,
            CustomerLookupService customerLookupService)
        {
            _db = db;

            _megaprintService = megaprintService;

            _xmlBuilder = xmlBuilder;

            _customerLookupService =
                customerLookupService;
        }

        public FelResult CertifyMock(
            string quickBooksId,
            string invoiceNumber)
        {
            return new FelResult
            {
                Serie = "TEST",

                DteNumber =
                    string.IsNullOrWhiteSpace(invoiceNumber)
                        ? quickBooksId
                        : invoiceNumber,

                AuthorizationNumber =
                    Guid.NewGuid()
                        .ToString()
                        .ToUpperInvariant(),

                CertificationDate =
                    DateTime.UtcNow,

                Qr = "",

                CustomerNit = "CF",

                CustomerName =
                    "Consumidor Final",

                CertifierName =
                    "MEGAPRINT",

                CertifierNit = ""
            };
        }

        public async Task<FelResult> CertifyAsync(
            string quickBooksId,
            string quickBooksJson,
            string saleType,
            string? nitOverride,
            string? customerNameOverride,
            IReadOnlyCollection<ItemDiscountRequest>? discounts,
            string priceType = "contado",
            decimal creditPercentage = 0m)
        {
            quickBooksId =
                (quickBooksId ?? "")
                    .Trim();

            if (string.IsNullOrWhiteSpace(
                quickBooksId))
            {
                throw new Exception(
                    "El ID de QuickBooks es obligatorio."
                );
            }

            if (string.IsNullOrWhiteSpace(
                quickBooksJson))
            {
                throw new Exception(
                    "No se recibió el documento de QuickBooks."
                );
            }

            Invoice? existing =
                await _db.Invoices
                    .Include(x => x.Lines)
                    .Where(x =>
                        x.QuickBooksId ==
                            quickBooksId &&
                        x.IsCertified
                    )
                    .OrderByDescending(x =>
                        x.CreatedAt
                    )
                    .FirstOrDefaultAsync();

            /*
             * Si ya está certificada, se reutilizan
             * exactamente los datos guardados.
             */
            if (existing != null)
            {
                if (existing.IsCancelled)
                {
                    throw new Exception(
                        "La factura está anulada y no puede imprimirse como vigente."
                    );
                }

                string existingQr =
                    existing.FelQr ?? "";

                if (string.IsNullOrWhiteSpace(
                        existingQr) &&
                    !string.IsNullOrWhiteSpace(
                        existing
                            .FelAuthorizationNumber))
                {
                    existingQr =
                        existing
                            .FelAuthorizationNumber
                            .Trim();

                    existing.FelQr =
                        existingQr;

                    await _db.SaveChangesAsync();
                }

                return new FelResult
                {
                    Serie =
                        existing.FelSerie ?? "",

                    DteNumber =
                        existing.FelDteNumber ?? "",

                    AuthorizationNumber =
                        existing
                            .FelAuthorizationNumber
                        ?? "",

                    CertificationDate =
                        existing
                            .FelCertificationDate
                        ?? DateTime.UtcNow,

                    Qr =
                        existing.FelQr ?? "",

                    CustomerNit =
                        string.IsNullOrWhiteSpace(
                            existing.CustomerNit)
                            ? "CF"
                            : existing.CustomerNit,

                    CustomerName =
                        string.IsNullOrWhiteSpace(
                            existing.CustomerName)
                            ? "Consumidor Final"
                            : existing.CustomerName,

                    CertifierName =
                        existing
                            .FelCertifierName
                        ?? "",

                    CertifierNit =
                        existing
                            .FelCertifierNit
                        ?? ""
                };
            }

            string nitNormalizado =
                string.IsNullOrWhiteSpace(
                    nitOverride)
                    ? "CF"
                    : nitOverride
                        .Trim()
                        .Replace("-", "")
                        .Replace(" ", "");

            string nombreFiscalNormalizado =
                string.IsNullOrWhiteSpace(
                    customerNameOverride)
                    ? ""
                    : customerNameOverride
                        .Trim();

            if (nitNormalizado.Equals(
                "CF",
                StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(
                    nombreFiscalNormalizado))
                {
                    nombreFiscalNormalizado =
                        "Consumidor Final";
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(
                    nombreFiscalNormalizado))
                {
                    throw new Exception(
                        "Debe verificar el NIT antes de certificar."
                    );
                }

                if (nombreFiscalNormalizado.Equals(
                    "Consumidor Final",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        "El nombre fiscal no corresponde al NIT indicado."
                    );
                }
            }

            discounts ??=
                new List<ItemDiscountRequest>();

            ValidarDescuentos(
                discounts
            );

            priceType =
                NormalizarTipoPrecio(
                    priceType,
                    saleType
                );

            creditPercentage =
                NormalizarPorcentajeCredito(
                    priceType,
                    creditPercentage
                );

            string xmlSinFirmar =
                _xmlBuilder.BuildFactXml(
                    quickBooksJson,
                    nitNormalizado,
                    nombreFiscalNormalizado,
                    discounts,
                    priceType,
                    creditPercentage
                );

            string token =
                await _megaprintService
                    .SolicitarTokenAsync();

            string xmlFirmado =
                await _megaprintService
                    .SolicitarFirmaAsync(
                        xmlSinFirmar,
                        token
                    );

            var registro =
                await _megaprintService
                    .RegistrarDocumentoAsync(
                        xmlFirmado,
                        token
                    );

            string xmlCertificado =
                registro.xmlCertificado
                ?? "";

            string uuid =
                registro.uuid
                ?? "";

            if (string.IsNullOrWhiteSpace(
                uuid))
            {
                throw new Exception(
                    "Megaprint no devolvió el número de autorización FEL."
                );
            }

            string qrValue =
                uuid;

            var serieNumero =
                ExtractSerieYNumero(
                    uuid
                );

            string serie =
                serieNumero.serie;

            string numero =
                serieNumero.numero;

            var certificador =
                ExtractCertificador(
                    xmlCertificado
                );

            string certifierName =
                certificador.certifierName;

            string certifierNit =
                certificador.certifierNit;

            DateTime certificationDate =
                DateTime.UtcNow;

            var resumen =
                ParseResumen(
                    quickBooksJson,
                    discounts,
                    nombreFiscalNormalizado,
                    priceType,
                    creditPercentage
                );

            List<InvoiceLine> lineas =
                ParseLineasFactura(
                    quickBooksJson,
                    discounts,
                    priceType,
                    creditPercentage
                );

            decimal subtotalGuardado =
                lineas.Count > 0
                    ? lineas.Sum(x =>
                        x.OriginalSubtotal)
                    : resumen.totalOriginal;

            decimal descuentoGuardado =
                lineas.Count > 0
                    ? lineas.Sum(x =>
                        x.DiscountAmount)
                    : resumen.discountTotal;

            decimal totalGuardado =
                subtotalGuardado -
                descuentoGuardado;

            if (totalGuardado < 0m)
            {
                totalGuardado = 0m;
            }

            string customerNit =
                ObtenerNitCliente(
                    nitNormalizado,
                    resumen.customerName
                );

            string customerName =
                string.IsNullOrWhiteSpace(
                    resumen.customerName)
                    ? "Consumidor Final"
                    : resumen.customerName
                        .Trim();

            Invoice? invoice =
                await _db.Invoices
                    .Include(x => x.Lines)
                    .Where(x =>
                        x.QuickBooksId ==
                            quickBooksId
                    )
                    .OrderByDescending(x =>
                        x.CreatedAt
                    )
                    .FirstOrDefaultAsync();

            if (invoice == null)
            {
                invoice =
                    new Invoice
                    {
                        QuickBooksId =
                            quickBooksId,

                        CreatedAt =
                            DateTime.UtcNow
                    };

                _db.Invoices.Add(
                    invoice
                );
            }
            else
            {
                if (invoice.IsCancelled)
                {
                    throw new Exception(
                        "La factura está anulada y no puede certificarse."
                    );
                }

                if (invoice.Lines.Count > 0)
                {
                    _db.InvoiceLines
                        .RemoveRange(
                            invoice.Lines
                        );
                }
            }

            invoice.InvoiceNumber =
                resumen.docNumber;

            invoice.CustomerName =
                customerName;

            invoice.CustomerNit =
                customerNit;

            invoice.IssueDate =
                resumen.issueDate;

            invoice.Subtotal =
                subtotalGuardado;

            invoice.DiscountTotal =
                descuentoGuardado;

            invoice.Total =
                totalGuardado;

            invoice.SaleType =
                string.IsNullOrWhiteSpace(
                    saleType)
                    ? "contado"
                    : saleType
                        .Trim()
                        .ToLowerInvariant();

            invoice.PriceType =
                priceType;

            invoice.CreditPercentage =
                creditPercentage;

            invoice.Status =
                "certified";

            invoice.FelSerie =
                serie;

            invoice.FelDteNumber =
                numero;

            invoice.FelAuthorizationNumber =
                uuid;

            invoice.FelCertificationDate =
                certificationDate;

            invoice.FelQr =
                qrValue;

            invoice.FelCertifierName =
                certifierName;

            invoice.FelCertifierNit =
                certifierNit;

            invoice.IsCertified =
                true;

            invoice.IsCancelled =
                false;

            invoice.CancellationReason =
                "";

            invoice.CancellationDate =
                null;

            invoice.FelCancellationAuthorizationNumber =
                "";

            invoice.FelCancellationXml =
                "";

            invoice.Lines =
                lineas;

            await _db.SaveChangesAsync();

            return new FelResult
            {
                Serie =
                    serie,

                DteNumber =
                    numero,

                AuthorizationNumber =
                    uuid,

                CertificationDate =
                    certificationDate,

                Qr =
                    qrValue,

                CustomerNit =
                    customerNit,

                CustomerName =
                    customerName,

                CertifierName =
                    certifierName,

                CertifierNit =
                    certifierNit
            };
        }

        /*
         * Sobrecarga para conservar compatibilidad
         * con llamadas antiguas.
         */
        public Task<FelResult> CertifyAsync(
            string quickBooksId,
            string quickBooksJson,
            string saleType,
            string? nitOverride = null,
            decimal descuento = 0m)
        {
            if (descuento < 0m)
            {
                throw new Exception(
                    "El descuento no puede ser negativo."
                );
            }

            if (descuento > 0m)
            {
                throw new Exception(
                    "El descuento general ya no está permitido. " +
                    "Debe aplicarse a un producto específico."
                );
            }

            string priceType =
                saleType.Equals(
                    "credito",
                    StringComparison.OrdinalIgnoreCase)
                    ? "credito"
                    : "contado";

            decimal creditPercentage =
                priceType == "credito"
                    ? 3m
                    : 0m;

            return CertifyAsync(
                quickBooksId,
                quickBooksJson,
                saleType,
                nitOverride,
                null,
                Array.Empty<ItemDiscountRequest>(),
                priceType,
                creditPercentage
            );
        }

        private static string NormalizarTipoPrecio(
            string? priceType,
            string? saleType)
        {
            string normalized =
                (priceType ?? "")
                    .Trim()
                    .ToLowerInvariant();

            normalized =
                normalized
                    .Replace("é", "e")
                    .Replace("í", "i");

            if (string.IsNullOrWhiteSpace(
                normalized))
            {
                normalized =
                    string.Equals(
                        saleType,
                        "credito",
                        StringComparison.OrdinalIgnoreCase)
                        ? "credito"
                        : "contado";
            }

            if (normalized != "contado" &&
                normalized != "credito")
            {
                throw new Exception(
                    "El tipo de precio debe ser contado o crédito."
                );
            }

            return normalized;
        }

        private static decimal
            NormalizarPorcentajeCredito(
                string priceType,
                decimal creditPercentage)
        {
            if (priceType == "contado")
            {
                return 0m;
            }

            if (creditPercentage <= 0m)
            {
                return 3m;
            }

            if (creditPercentage != 3m)
            {
                throw new Exception(
                    "El porcentaje de precio crédito debe ser 3%."
                );
            }

            return creditPercentage;
        }

        private static void ValidarDescuentos(
            IReadOnlyCollection<ItemDiscountRequest> discounts)
        {
            var lineIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (
                ItemDiscountRequest discount
                in discounts)
            {
                if (discount == null)
                {
                    continue;
                }

                string lineId =
                    discount.LineId?.Trim()
                    ?? "";

                if (string.IsNullOrWhiteSpace(
                    lineId))
                {
                    throw new Exception(
                        "Todo descuento debe indicar el LineId."
                    );
                }

                if (discount.Amount < 0m)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} no puede ser negativo."
                    );
                }

                if (!lineIds.Add(
                    lineId))
                {
                    throw new Exception(
                        $"La línea {lineId} está repetida en la lista de descuentos."
                    );
                }
            }
        }

        private string ObtenerNitCliente(
            string? nitOverride,
            string customerName)
        {
            string customerNit;

            if (!string.IsNullOrWhiteSpace(
                nitOverride))
            {
                customerNit =
                    nitOverride
                        .Trim()
                        .Replace("-", "")
                        .Replace(" ", "");
            }
            else
            {
                customerNit =
                    _customerLookupService
                        .GetNit(
                            customerName
                        );
            }

            if (string.IsNullOrWhiteSpace(
                customerNit))
            {
                customerNit =
                    "CF";
            }

            return customerNit;
        }

        private static List<InvoiceLine>
            ParseLineasFactura(
                string quickBooksJson,
                IReadOnlyCollection<ItemDiscountRequest> discounts,
                string priceType,
                decimal creditPercentage)
        {
            using JsonDocument jsonDocument =
                JsonDocument.Parse(
                    quickBooksJson
                );

            JsonElement queryResponse =
                jsonDocument.RootElement
                    .GetProperty(
                        "QueryResponse"
                    );

            JsonElement quickBooksDocument;

            if (queryResponse.TryGetProperty(
                "Invoice",
                out JsonElement invoices))
            {
                quickBooksDocument =
                    invoices[0];
            }
            else if (
                queryResponse.TryGetProperty(
                    "SalesReceipt",
                    out JsonElement receipts))
            {
                quickBooksDocument =
                    receipts[0];
            }
            else
            {
                throw new Exception(
                    "No se encontró Invoice ni SalesReceipt."
                );
            }

            /*
             * QuickBooks ya contiene el precio seleccionado por el
             * Dashboard. No se vuelve a aplicar el 3 % al certificar.
             */
            decimal priceFactor =
                1m;

            Dictionary<string, decimal>
                descuentosPorLinea =
                    discounts
                        .Where(x =>
                            x != null &&
                            !string.IsNullOrWhiteSpace(
                                x.LineId
                            )
                        )
                        .GroupBy(
                            x => x.LineId.Trim(),
                            StringComparer
                                .OrdinalIgnoreCase
                        )
                        .ToDictionary(
                            group => group.Key,
                            group => group.Sum(
                                x => x.Amount
                            ),
                            StringComparer
                                .OrdinalIgnoreCase
                        );

            var resultado =
                new List<InvoiceLine>();

            if (!quickBooksDocument
                    .TryGetProperty(
                        "Line",
                        out JsonElement lines) ||
                lines.ValueKind !=
                    JsonValueKind.Array)
            {
                return resultado;
            }

            foreach (
                JsonElement line
                in lines.EnumerateArray())
            {
                string detailType =
                    GetJsonString(
                        line,
                        "DetailType"
                    );

                if (!detailType.Equals(
                    "SalesItemLineDetail",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!line.TryGetProperty(
                    "SalesItemLineDetail",
                    out JsonElement salesDetail))
                {
                    continue;
                }

                string lineId =
                    GetJsonString(
                        line,
                        "Id"
                    );

                string description =
                    GetJsonString(
                        line,
                        "Description"
                    );

                string itemId =
                    "";

                if (salesDetail.TryGetProperty(
                    "ItemRef",
                    out JsonElement itemRef))
                {
                    itemId =
                        GetJsonString(
                            itemRef,
                            "value"
                        );

                    if (string.IsNullOrWhiteSpace(
                        description))
                    {
                        description =
                            GetJsonString(
                                itemRef,
                                "name"
                            );
                    }
                }

                decimal quantity =
                    GetJsonDecimal(
                        salesDetail,
                        "Qty"
                    );

                if (quantity <= 0m)
                {
                    quantity =
                        1m;
                }

                decimal originalUnitPrice =
                    GetJsonDecimal(
                        salesDetail,
                        "UnitPrice"
                    );

                decimal originalSubtotal =
                    GetJsonDecimal(
                        line,
                        "Amount"
                    );

                if (originalUnitPrice <= 0m &&
                    quantity > 0m)
                {
                    originalUnitPrice =
                        originalSubtotal /
                        quantity;
                }

                if (originalSubtotal <= 0m)
                {
                    originalSubtotal =
                        originalUnitPrice *
                        quantity;
                }

                decimal appliedUnitPrice =
                    Math.Round(
                        originalUnitPrice *
                        priceFactor,
                        2,
                        MidpointRounding.AwayFromZero
                    );

                decimal appliedSubtotal =
                    Math.Round(
                        appliedUnitPrice *
                        quantity,
                        2,
                        MidpointRounding.AwayFromZero
                    );

                decimal discountAmount =
                    0m;

                if (!string.IsNullOrWhiteSpace(
                        lineId) &&
                    descuentosPorLinea
                        .TryGetValue(
                            lineId,
                            out decimal foundDiscount))
                {
                    discountAmount =
                        foundDiscount;
                }

                if (discountAmount < 0m)
                {
                    discountAmount =
                        0m;
                }

                if (discountAmount >
                    appliedSubtotal)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} supera el subtotal del producto."
                    );
                }

                decimal finalTotal =
                    appliedSubtotal -
                    discountAmount;

                resultado.Add(
                    new InvoiceLine
                    {
                        QuickBooksLineId =
                            lineId,

                        QuickBooksItemId =
                            itemId,

                        Description =
                            description,

                        Quantity =
                            quantity,

                        OriginalUnitPrice =
                            originalUnitPrice,

                        AppliedUnitPrice =
                            appliedUnitPrice,

                        OriginalSubtotal =
                            appliedSubtotal,

                        DiscountAmount =
                            discountAmount,

                        FinalTotal =
                            finalTotal,

                        CreatedAt =
                            DateTime.UtcNow
                    }
                );
            }

            return resultado;
        }

        private static (
            string serie,
            string numero
        ) ExtractSerieYNumero(
            string uuid)
        {
            string clean =
                (uuid ?? "")
                    .Replace("-", "");

            string serie =
                clean.Length >= 8
                    ? clean.Substring(
                        0,
                        8
                    )
                    : clean;

            string numero =
                "";

            if (clean.Length >= 16)
            {
                string hexNumero =
                    clean.Substring(
                        8,
                        8
                    );

                numero =
                    Convert.ToInt64(
                        hexNumero,
                        16
                    )
                    .ToString();
            }

            return (
                serie,
                numero
            );
        }

        private static (
            string certifierName,
            string certifierNit
        ) ExtractCertificador(
            string xmlCertificado)
        {
            try
            {
                XDocument document =
                    XDocument.Parse(
                        xmlCertificado
                    );

                string name =
                    document.Descendants()
                        .FirstOrDefault(
                            element =>
                                element.Name.LocalName ==
                                "NombreCertificador"
                        )
                        ?.Value
                    ?? "";

                string nit =
                    document.Descendants()
                        .FirstOrDefault(
                            element =>
                                element.Name.LocalName ==
                                "NITCertificador"
                        )
                        ?.Value
                    ?? "";

                return (
                    name,
                    nit
                );
            }
            catch
            {
                return (
                    "",
                    ""
                );
            }
        }

        private static (
            string docNumber,
            string customerName,
            decimal totalOriginal,
            decimal discountTotal,
            decimal totalFinal,
            DateTime issueDate
        ) ParseResumen(
            string quickBooksJson,
            IReadOnlyCollection<ItemDiscountRequest> discounts,
            string? customerNameOverride,
            string priceType,
            decimal creditPercentage)
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    quickBooksJson
                );

            JsonElement query =
                document.RootElement
                    .GetProperty(
                        "QueryResponse"
                    );

            JsonElement qbDocument;

            if (query.TryGetProperty(
                "Invoice",
                out JsonElement invoices))
            {
                qbDocument =
                    invoices[0];
            }
            else if (
                query.TryGetProperty(
                    "SalesReceipt",
                    out JsonElement receipts))
            {
                qbDocument =
                    receipts[0];
            }
            else
            {
                throw new Exception(
                    "No se encontró Invoice ni SalesReceipt."
                );
            }

            string docNumber =
                GetJsonString(
                    qbDocument,
                    "DocNumber"
                );

            string customerName =
                "Consumidor Final";

            if (qbDocument.TryGetProperty(
                    "CustomerRef",
                    out JsonElement customerRef))
            {
                customerName =
                    GetJsonString(
                        customerRef,
                        "name"
                    );

                if (string.IsNullOrWhiteSpace(
                    customerName))
                {
                    customerName =
                        "Consumidor Final";
                }
            }

            if (!string.IsNullOrWhiteSpace(
                customerNameOverride))
            {
                customerName =
                    customerNameOverride
                        .Trim();
            }

            decimal quickBooksTotal =
                GetJsonDecimal(
                    qbDocument,
                    "TotalAmt"
                );

            /*
             * El total de QuickBooks ya refleja el tipo de precio.
             */
            decimal totalOriginal =
                Math.Round(
                    quickBooksTotal,
                    2,
                    MidpointRounding.AwayFromZero
                );

            decimal discountTotal =
                discounts
                    .Where(x =>
                        x != null
                    )
                    .Sum(x =>
                        x.Amount
                    );

            if (discountTotal < 0m)
            {
                discountTotal =
                    0m;
            }

            if (discountTotal >
                totalOriginal)
            {
                throw new Exception(
                    "El descuento total supera el total del documento."
                );
            }

            decimal totalFinal =
                totalOriginal -
                discountTotal;

            DateTime issueDate =
                DateTime.UtcNow;

            string issueDateText =
                GetJsonString(
                    qbDocument,
                    "TxnDate"
                );

            if (DateTime.TryParse(
                issueDateText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsedDate))
            {
                issueDate =
                    DateTime.SpecifyKind(
                        parsedDate,
                        DateTimeKind.Utc
                    );
            }

            return (
                docNumber,
                customerName,
                totalOriginal,
                discountTotal,
                totalFinal,
                issueDate
            );
        }

        private static string GetJsonString(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement value))
            {
                return "";
            }

            if (value.ValueKind ==
                JsonValueKind.String)
            {
                return value.GetString()
                    ?? "";
            }

            return value.ToString();
        }

        private static decimal GetJsonDecimal(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement value))
            {
                return 0m;
            }

            if (value.ValueKind ==
                    JsonValueKind.Number &&
                value.TryGetDecimal(
                    out decimal number))
            {
                return number;
            }

            string text =
                value.ToString();

            return decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimal parsed)
                    ? parsed
                    : 0m;
        }
    }
}