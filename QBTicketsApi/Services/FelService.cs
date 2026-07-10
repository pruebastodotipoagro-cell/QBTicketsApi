using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Xml.Linq;
using System.Linq;
using QBTicketsApi.Database;
using QBTicketsApi.Models;

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

        // Se mantiene temporalmente por compatibilidad; ya no se usa en el flujo real (paso 6 lo reemplaza)
        public FelResult CertifyMock(string quickBooksId, string invoiceNumber)
        {
            return new FelResult
            {
                Serie = "TEST",
                DteNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? quickBooksId : invoiceNumber,
                AuthorizationNumber = Guid.NewGuid().ToString().ToUpper(),
                CertificationDate = DateTime.UtcNow,
                Qr = ""
            };
        }

        /// <summary>
        /// Certifica un documento ante Megaprint/SAT, o devuelve la certificación
        /// ya existente si este QuickBooksId ya fue certificado antes (idempotencia).
        /// </summary>
        public async Task<FelResult> CertifyAsync(string quickBooksId, string quickBooksJson, string saleType, string? nitOverride = null, decimal descuento = 0)
        {
            var existing = await _db.Invoices
                .FirstOrDefaultAsync(i => i.QuickBooksId == quickBooksId && i.IsCertified);

            if (existing != null)
            {
                return new FelResult
                {
                    Serie = existing.FelSerie,
                    DteNumber = existing.FelDteNumber,
                    AuthorizationNumber = existing.FelAuthorizationNumber,
                    CertificationDate = existing.FelCertificationDate ?? DateTime.UtcNow,
                    Qr = existing.FelQr,
                    CustomerNit = existing.CustomerNit,
                    CertifierName = existing.FelCertifierName,
                    CertifierNit = existing.FelCertifierNit
                };
            }

            // No existe todavía: certificamos de verdad contra Megaprint
            var xmlSinFirmar = _xmlBuilder.BuildFactXml(quickBooksJson, nitOverride, descuento);
            var token = await _megaprintService.SolicitarTokenAsync();
            var xmlFirmado = await _megaprintService.SolicitarFirmaAsync(xmlSinFirmar, token);
            var (xmlCertificado, uuid) = await _megaprintService.RegistrarDocumentoAsync(xmlFirmado, token);

            var (serie, numero) = ExtractSerieYNumero(uuid);
            var (certifierName, certifierNit) = ExtractCertificador(xmlCertificado);
            var certificationDate = DateTime.UtcNow;

            var (docNumber, customerName, total, issueDate) = ParseResumen(quickBooksJson);
            var customerNit = !string.IsNullOrWhiteSpace(nitOverride)
                ? nitOverride
                : _customerLookupService.GetNit(customerName);
            if (string.IsNullOrWhiteSpace(customerNit)) customerNit = "CF";

            var invoice = new Invoice
            {
                QuickBooksId = quickBooksId,
                InvoiceNumber = docNumber,
                CustomerName = customerName,
                CustomerNit = customerNit,
                IssueDate = issueDate,
                Total = total,
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

        // Serie = primeros 8 caracteres hex del UUID.
        // Numero = valor decimal de los caracteres 9-16 del UUID (sin guiones), según el Manual de Implementación 2.0 de Megaprint.
        private static (string serie, string numero) ExtractSerieYNumero(string uuid)
        {
            string clean = uuid.Replace("-", "");
            string serie = clean.Length >= 8 ? clean.Substring(0, 8) : clean;
            string numero = "";

            if (clean.Length >= 16)
            {
                string hexNumero = clean.Substring(8, 8);
                numero = Convert.ToInt64(hexNumero, 16).ToString();
            }

            return (serie, numero);
        }

        // Extrae el nombre y NIT del certificador reales desde el XML ya certificado por Megaprint,
        // en vez de dejarlos fijos como texto en el ticket.
        private static (string certifierName, string certifierNit) ExtractCertificador(string xmlCertificado)
        {
            try
            {
                var doc = XDocument.Parse(xmlCertificado);

                string name = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "NombreCertificador")?.Value ?? "";

                string nit = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "NITCertificador")?.Value ?? "";

                return (name, nit);
            }
            catch
            {
                return ("", "");
            }
        }

        private static (string docNumber, string customerName, decimal total, DateTime issueDate) ParseResumen(string quickBooksJson)
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

            string docNumber = qbDoc.TryGetProperty("DocNumber", out var dn) ? dn.GetString() ?? "" : "";

            string customerName = "Consumidor Final";
            if (qbDoc.TryGetProperty("CustomerRef", out var customerRef) &&
                customerRef.TryGetProperty("name", out var name))
                customerName = name.GetString() ?? "Consumidor Final";

            decimal total = qbDoc.TryGetProperty("TotalAmt", out var totalEl) && totalEl.TryGetDecimal(out var t) ? t : 0;

            DateTime issueDate = qbDoc.TryGetProperty("TxnDate", out var txnDateEl) &&
                                 DateTime.TryParse(txnDateEl.GetString(), out var parsedDate)
                ? DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc)
                : DateTime.UtcNow;

            return (docNumber, customerName, total, issueDate);
        }
    }
}