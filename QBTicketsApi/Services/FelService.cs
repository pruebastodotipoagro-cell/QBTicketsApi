using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Xml.Linq;
using System.Linq;
using QBTicketsApi.Database;
using QBTicketsApi.Models;
using QBTicketsApi.DTOs;

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
            _customerLookupService = customerLookupService;
        }

        public FelResult CertifyMock(
            string quickBooksId,
            string invoiceNumber)
        {
            return new FelResult
            {
                Serie = "TEST",
                DteNumber = string.IsNullOrWhiteSpace(invoiceNumber)
                    ? quickBooksId
                    : invoiceNumber,

                AuthorizationNumber =
                    Guid.NewGuid().ToString().ToUpperInvariant(),

                CertificationDate = DateTime.UtcNow,
                Qr = ""
            };
        }

        /// <summary>
        /// Nuevo método: certifica usando descuentos por línea de producto.
        /// </summary>
        public async Task<FelResult> CertifyAsync(
            string quickBooksId,
            string quickBooksJson,
            string saleType,
            string? nitOverride,
            string? customerNameOverride,
            IReadOnlyCollection<ItemDiscountRequest>? discounts)
        {
            var existing = await _db.Invoices
                .FirstOrDefaultAsync(
                    i => i.QuickBooksId == quickBooksId &&
                         i.IsCertified
                );

            // Si ya fue certificado, no se vuelve a enviar a Megaprint.
            if (existing != null)
            {
                return new FelResult
                {
                    Serie = existing.FelSerie,
                    DteNumber = existing.FelDteNumber,
                    AuthorizationNumber =
                        existing.FelAuthorizationNumber,

                    CertificationDate =
                        existing.FelCertificationDate
                        ?? DateTime.UtcNow,

                    Qr = existing.FelQr,
                    CustomerNit = existing.CustomerNit,
                    CertifierName = existing.FelCertifierName,
                    CertifierNit = existing.FelCertifierNit
                };
            }

            discounts ??=
                new List<ItemDiscountRequest>();

            ValidarDescuentos(discounts);

            /*
             * Construimos el XML aplicando el descuento solamente
             * a las líneas seleccionadas.
             */
            string xmlSinFirmar =
                _xmlBuilder.BuildFactXml(
                    quickBooksJson,
                    nitOverride,
                    customerNameOverride,
                    discounts
                );

            string token =
                await _megaprintService.SolicitarTokenAsync();

            string xmlFirmado =
                await _megaprintService.SolicitarFirmaAsync(
                    xmlSinFirmar,
                    token
                );

            var registro =
                await _megaprintService.RegistrarDocumentoAsync(
                    xmlFirmado,
                    token
                );

            string xmlCertificado =
                registro.xmlCertificado;

            string uuid =
                registro.uuid;

            var serieNumero =
                ExtractSerieYNumero(uuid);

            string serie =
                serieNumero.serie;

            string numero =
                serieNumero.numero;

            var certificador =
                ExtractCertificador(xmlCertificado);

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
                    customerNameOverride
                );

            string customerNit =
                ObtenerNitCliente(
                    nitOverride,
                    resumen.customerName
                );

            var invoice = new Invoice
            {
                QuickBooksId = quickBooksId,
                InvoiceNumber = resumen.docNumber,
                CustomerName = resumen.customerName,
                CustomerNit = customerNit,
                IssueDate = resumen.issueDate,

                // Guardamos el total final después del descuento.
                Total = resumen.totalFinal,

                SaleType = saleType,
                Status = "certified",

                FelSerie = serie,
                FelDteNumber = numero,
                FelAuthorizationNumber = uuid,
                FelCertificationDate = certificationDate,
                FelQr = "",

                FelCertifierName = certifierName,
                FelCertifierNit = certifierNit,

                IsCertified = true
            };

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();

            return new FelResult
            {
                Serie = serie,
                DteNumber = numero,
                AuthorizationNumber = uuid,
                CertificationDate = certificationDate,
                Qr = "",
                CustomerNit = customerNit,
                CertifierName = certifierName,
                CertifierNit = certifierNit
            };
        }

        /*
         * Método temporal para que el controlador anterior siga compilando.
         * Cuando cambiemos TicketPdfController al nuevo POST,
         * este método se podrá eliminar.
         */
        public Task<FelResult> CertifyAsync(
            string quickBooksId,
            string quickBooksJson,
            string saleType,
            string? nitOverride = null,
            decimal descuento = 0)
        {
            if (descuento < 0)
            {
                throw new Exception(
                    "El descuento no puede ser negativo."
                );
            }

            if (descuento > 0)
            {
                throw new Exception(
                    "El descuento general ya no está permitido. " +
                    "Debe aplicarse a un producto específico."
                );
            }

            return CertifyAsync(
                quickBooksId,
                quickBooksJson,
                saleType,
                nitOverride,
                null,
                new List<ItemDiscountRequest>()
            );
        }

        private static void ValidarDescuentos(
            IReadOnlyCollection<ItemDiscountRequest> discounts)
        {
            var lineIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

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
                        "Todo descuento debe indicar el LineId."
                    );
                }

                if (discount.Amount < 0)
                {
                    throw new Exception(
                        $"El descuento de la línea {lineId} " +
                        "no puede ser negativo."
                    );
                }

                if (!lineIds.Add(lineId))
                {
                    throw new Exception(
                        $"La línea {lineId} está repetida " +
                        "en la lista de descuentos."
                    );
                }
            }
        }

        private string ObtenerNitCliente(
            string? nitOverride,
            string customerName)
        {
            string customerNit;

            if (!string.IsNullOrWhiteSpace(nitOverride))
            {
                customerNit =
                    nitOverride.Trim().Replace("-", "");
            }
            else
            {
                customerNit =
                    _customerLookupService.GetNit(customerName);
            }

            if (string.IsNullOrWhiteSpace(customerNit))
            {
                customerNit = "CF";
            }

            return customerNit;
        }

        private static (
            string serie,
            string numero
        ) ExtractSerieYNumero(string uuid)
        {
            string clean =
                (uuid ?? "").Replace("-", "");

            string serie =
                clean.Length >= 8
                    ? clean.Substring(0, 8)
                    : clean;

            string numero = "";

            if (clean.Length >= 16)
            {
                string hexNumero =
                    clean.Substring(8, 8);

                numero =
                    Convert.ToInt64(
                        hexNumero,
                        16
                    )
                    .ToString();
            }

            return (serie, numero);
        }

        private static (
            string certifierName,
            string certifierNit
        ) ExtractCertificador(string xmlCertificado)
        {
            try
            {
                var doc =
                    XDocument.Parse(xmlCertificado);

                string name =
                    doc.Descendants()
                        .FirstOrDefault(
                            e => e.Name.LocalName ==
                                 "NombreCertificador"
                        )
                        ?.Value
                    ?? "";

                string nit =
                    doc.Descendants()
                        .FirstOrDefault(
                            e => e.Name.LocalName ==
                                 "NITCertificador"
                        )
                        ?.Value
                    ?? "";

                return (name, nit);
            }
            catch
            {
                return ("", "");
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
            string? customerNameOverride)
        {
            using var doc =
                JsonDocument.Parse(quickBooksJson);

            var query =
                doc.RootElement.GetProperty(
                    "QueryResponse"
                );

            JsonElement qbDoc;

            if (query.TryGetProperty(
                "Invoice",
                out var invoices))
            {
                qbDoc = invoices[0];
            }
            else if (query.TryGetProperty(
                "SalesReceipt",
                out var receipts))
            {
                qbDoc = receipts[0];
            }
            else
            {
                throw new Exception(
                    "No se encontró Invoice ni SalesReceipt."
                );
            }

            string docNumber = "";

            if (qbDoc.TryGetProperty(
                    "DocNumber",
                    out var docNumberElement))
            {
                docNumber =
                    docNumberElement.GetString() ?? "";
            }

            string customerName =
                "Consumidor Final";

            if (qbDoc.TryGetProperty(
                    "CustomerRef",
                    out var customerRef) &&
                customerRef.TryGetProperty(
                    "name",
                    out var nameElement))
            {
                customerName =
                    nameElement.GetString()
                    ?? "Consumidor Final";
            }

            if (!string.IsNullOrWhiteSpace(
                customerNameOverride))
            {
                customerName =
                    customerNameOverride.Trim();
            }

            decimal totalOriginal = 0;

            if (qbDoc.TryGetProperty(
                    "TotalAmt",
                    out var totalElement))
            {
                totalElement.TryGetDecimal(
                    out totalOriginal
                );
            }

            decimal discountTotal =
                discounts
                    .Where(x => x != null)
                    .Sum(x => x.Amount);

            if (discountTotal < 0)
            {
                discountTotal = 0;
            }

            if (discountTotal > totalOriginal)
            {
                throw new Exception(
                    "El descuento total supera el total " +
                    "del documento."
                );
            }

            decimal totalFinal =
                totalOriginal - discountTotal;

            DateTime issueDate;

            if (qbDoc.TryGetProperty(
                    "TxnDate",
                    out var txnDateElement) &&
                DateTime.TryParse(
                    txnDateElement.GetString(),
                    out var parsedDate))
            {
                issueDate =
                    DateTime.SpecifyKind(
                        parsedDate,
                        DateTimeKind.Utc
                    );
            }
            else
            {
                issueDate = DateTime.UtcNow;
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
    }
}