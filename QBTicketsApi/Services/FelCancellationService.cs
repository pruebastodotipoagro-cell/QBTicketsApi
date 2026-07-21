using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using QBTicketsApi.Models;

namespace QBTicketsApi.Services
{
    public class FelCancellationResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = "";

        public string QuickBooksId { get; set; } = "";

        public string OriginalAuthorizationNumber { get; set; } = "";

        public string CancellationAuthorizationNumber { get; set; } = "";

        public string CancellationReason { get; set; } = "";

        public DateTime CancellationDate { get; set; }
    }

    public class FelCancellationService
    {
        private readonly AppDbContext _db;
        private readonly MegaprintService _megaprintService;
        private readonly FelCancellationXmlBuilderService
            _xmlBuilder;
        private readonly IConfiguration _config;

        public FelCancellationService(
            AppDbContext db,
            MegaprintService megaprintService,
            FelCancellationXmlBuilderService xmlBuilder,
            IConfiguration config)
        {
            _db = db;
            _megaprintService = megaprintService;
            _xmlBuilder = xmlBuilder;
            _config = config;
        }

        public async Task<FelCancellationResult>
            CancelAsync(
                string quickBooksId,
                string reason)
        {
            quickBooksId =
                (quickBooksId ?? "")
                    .Trim();

            reason =
                (reason ?? "")
                    .Trim();

            if (string.IsNullOrWhiteSpace(
                quickBooksId))
            {
                throw new Exception(
                    "No se recibió el ID de QuickBooks."
                );
            }

            if (string.IsNullOrWhiteSpace(
                reason))
            {
                throw new Exception(
                    "Debe indicar el motivo de la anulación."
                );
            }

            if (reason.Length < 5)
            {
                throw new Exception(
                    "El motivo de anulación debe tener " +
                    "al menos 5 caracteres."
                );
            }

            if (reason.Length > 255)
            {
                throw new Exception(
                    "El motivo de anulación no puede superar " +
                    "los 255 caracteres."
                );
            }

            Invoice? invoice =
                await _db.Invoices
                    .FirstOrDefaultAsync(
                        currentInvoice =>
                            currentInvoice.QuickBooksId ==
                            quickBooksId
                    );

            if (invoice == null)
            {
                throw new Exception(
                    "La factura no se encuentra guardada " +
                    "en la base de datos."
                );
            }

            if (!invoice.IsCertified)
            {
                throw new Exception(
                    "La factura no está certificada y no " +
                    "puede anularse como documento FEL."
                );
            }

            if (invoice.IsCancelled)
            {
                return new FelCancellationResult
                {
                    Success =
                        true,

                    Message =
                        "La factura ya se encuentra anulada.",

                    QuickBooksId =
                        invoice.QuickBooksId,

                    OriginalAuthorizationNumber =
                        invoice.FelAuthorizationNumber ?? "",

                    CancellationAuthorizationNumber =
                        invoice
                            .FelCancellationAuthorizationNumber
                        ?? "",

                    CancellationReason =
                        invoice.CancellationReason ?? "",

                    CancellationDate =
                        invoice.CancellationDate
                        ?? DateTime.UtcNow
                };
            }

            string authorizationNumber =
                invoice.FelAuthorizationNumber
                ?? "";

            if (string.IsNullOrWhiteSpace(
                authorizationNumber))
            {
                throw new Exception(
                    "La factura no tiene número de " +
                    "autorización FEL."
                );
            }

            string issuerNit =
                _config["Fel:IssuerNit"]
                ?? "";

            issuerNit =
                issuerNit
                    .Trim()
                    .Replace("-", "")
                    .Replace(" ", "");

            if (string.IsNullOrWhiteSpace(
                issuerNit))
            {
                throw new Exception(
                    "No está configurada la variable " +
                    "Fel:IssuerNit."
                );
            }

            string receiverNit =
                string.IsNullOrWhiteSpace(
                    invoice.CustomerNit
                )
                    ? "CF"
                    : invoice.CustomerNit
                        .Trim()
                        .Replace("-", "")
                        .Replace(" ", "");

            string token =
                await _megaprintService
                    .SolicitarTokenAsync();

            /*
             * Recuperamos el XML original certificado
             * para obtener la fecha y hora exactas
             * con las que se certificó el documento.
             */
            string certifiedXml =
                await _megaprintService
                    .RetornarXmlAsync(
                        authorizationNumber,
                        token
                    );

            DateTime originalIssueDate =
                _xmlBuilder
                    .ExtractOriginalIssueDate(
                        certifiedXml
                    );

            string cancellationXml =
                _xmlBuilder
                    .BuildCancellationXml(
                        authorizationNumber,
                        issuerNit,
                        receiverNit,
                        originalIssueDate,
                        reason
                    );

            var cancellationResponse =
                await _megaprintService
                    .AnularDocumentoAsync(
                        cancellationXml,
                        token
                    );

            string cancellationUuid =
                cancellationResponse
                    .uuidAnulacion;

            string certifiedCancellationXml =
                cancellationResponse
                    .xmlAnulacionCertificado;

            if (string.IsNullOrWhiteSpace(
                cancellationUuid))
            {
                throw new Exception(
                    "Megaprint no devolvió el UUID " +
                    "de la anulación."
                );
            }

            DateTime cancellationDate =
                DateTime.UtcNow;

            invoice.IsCancelled =
                true;

            invoice.Status =
                "cancelled";

            invoice.CancellationReason =
                reason;

            invoice.CancellationDate =
                cancellationDate;

            invoice.FelCancellationAuthorizationNumber =
                cancellationUuid;

            invoice.FelCancellationXml =
                certifiedCancellationXml ?? "";

            await _db.SaveChangesAsync();

            return new FelCancellationResult
            {
                Success =
                    true,

                Message =
                    "La factura fue anulada correctamente.",

                QuickBooksId =
                    invoice.QuickBooksId,

                OriginalAuthorizationNumber =
                    authorizationNumber,

                CancellationAuthorizationNumber =
                    cancellationUuid,

                CancellationReason =
                    reason,

                CancellationDate =
                    cancellationDate
            };
        }
    }
}