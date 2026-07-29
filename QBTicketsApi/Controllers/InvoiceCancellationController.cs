using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.Models;
using QBTicketsApi.Services;
using System.Globalization;
using System.Text.Json;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/invoices")]
    public class InvoiceCancellationController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly QuickBooksService _quickBooksService;
        private readonly FelCancellationService _felCancellationService;

        public InvoiceCancellationController(
            AppDbContext db,
            QuickBooksService quickBooksService,
            FelCancellationService felCancellationService)
        {
            _db = db;
            _quickBooksService = quickBooksService;
            _felCancellationService = felCancellationService;
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelInvoice(
            string id,
            [FromBody] CancelInvoiceRequest? request)
        {
            try
            {
                id = (id ?? "").Trim();

                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "El ID de la venta es obligatorio."
                    });
                }

                string reason =
                    (request?.Reason ?? "").Trim();

                if (string.IsNullOrWhiteSpace(reason))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Debe indicar el motivo de la anulación."
                    });
                }

                if (reason.Length < 5)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "El motivo de anulación debe tener al menos 5 caracteres."
                    });
                }

                if (reason.Length > 255)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "El motivo de anulación no puede superar 255 caracteres."
                    });
                }

                Invoice? storedInvoice =
                    await _db.Invoices
                        .Include(x => x.Lines)
                        .Where(x =>
                            x.QuickBooksId == id
                        )
                        .OrderByDescending(x =>
                            x.CreatedAt
                        )
                        .FirstOrDefaultAsync();

                if (storedInvoice != null &&
                    storedInvoice.IsCancelled)
                {
                    string quickBooksMessageAlreadyCancelled =
                        await _quickBooksService
                            .CancelDocumentInQuickBooksAsync(
                                id
                            );

                    return Ok(new
                    {
                        success = true,
                        message =
                            "La venta ya estaba anulada en el sistema. " +
                            quickBooksMessageAlreadyCancelled,
                        quickBooksId =
                            storedInvoice.QuickBooksId,
                        isCertified =
                            storedInvoice.IsCertified,
                        cancellationReason =
                            storedInvoice.CancellationReason,
                        cancellationDate =
                            storedInvoice.CancellationDate,
                        cancellationAuthorizationNumber =
                            storedInvoice
                                .FelCancellationAuthorizationNumber
                    });
                }

                /*
                 * Si ya está certificada, la anulación debe
                 * enviarse obligatoriamente a Megaprint.
                 */
                if (storedInvoice != null &&
                    storedInvoice.IsCertified)
                {
                    FelCancellationResult felResult =
                        await _felCancellationService
                            .CancelAsync(
                                id,
                                reason
                            );

                    string quickBooksMessageCertified =
                        await _quickBooksService
                            .CancelDocumentInQuickBooksAsync(
                                id
                            );

                    return Ok(new
                    {
                        success =
                            felResult.Success,

                        message =
                            felResult.Message +
                            " " +
                            quickBooksMessageCertified,

                        quickBooksId =
                            felResult.QuickBooksId,

                        isCertified =
                            true,

                        cancellationReason =
                            felResult.CancellationReason,

                        cancellationDate =
                            felResult.CancellationDate,

                        cancellationAuthorizationNumber =
                            felResult
                                .CancellationAuthorizationNumber
                    });
                }

                /*
                 * Documento no certificado:
                 * se anula únicamente dentro del sistema.
                 */
                string quickBooksJson =
                    await ObtenerDocumentoQuickBooksAsync(
                        id
                    );

                if (string.IsNullOrWhiteSpace(
                    quickBooksJson))
                {
                    return NotFound(new
                    {
                        success = false,
                        error =
                            "No se encontró la venta en QuickBooks."
                    });
                }

                QuickBooksDocumentSummary summary =
                    ParseQuickBooksDocument(
                        quickBooksJson
                    );

                if (storedInvoice == null)
                {
                    storedInvoice =
                        new Invoice
                        {
                            QuickBooksId =
                                id,

                            CreatedAt =
                                DateTime.UtcNow
                        };

                    _db.Invoices.Add(
                        storedInvoice
                    );
                }

                storedInvoice.InvoiceNumber =
                    summary.InvoiceNumber;

                storedInvoice.CustomerName =
                    summary.CustomerName;

                if (string.IsNullOrWhiteSpace(
                    storedInvoice.CustomerNit))
                {
                    storedInvoice.CustomerNit =
                        "CF";
                }

                storedInvoice.IssueDate =
                    summary.IssueDate;

                storedInvoice.Subtotal =
                    summary.Total;

                storedInvoice.DiscountTotal =
                    0m;

                storedInvoice.Total =
                    summary.Total;

                storedInvoice.SaleType =
                    summary.SaleType;

                if (string.IsNullOrWhiteSpace(
                    storedInvoice.PriceType))
                {
                    storedInvoice.PriceType =
                        summary.SaleType == "credito"
                            ? "credito"
                            : "contado";
                }

                if (storedInvoice.PriceType ==
                        "credito" &&
                    storedInvoice.CreditPercentage <= 0m)
                {
                    storedInvoice.CreditPercentage =
                        3m;
                }

                storedInvoice.IsCertified =
                    false;

                storedInvoice.IsCancelled =
                    true;

                storedInvoice.Status =
                    "cancelled";

                storedInvoice.CancellationReason =
                    reason;

                storedInvoice.CancellationDate =
                    DateTime.UtcNow;

                storedInvoice
                    .FelCancellationAuthorizationNumber =
                        "";

                storedInvoice.FelCancellationXml =
                    "";

                string quickBooksMessage =
                    await _quickBooksService
                        .CancelDocumentInQuickBooksAsync(
                            id
                        );

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message =
                        "La venta no certificada fue anulada correctamente. " +
                        quickBooksMessage,
                    quickBooksId =
                        storedInvoice.QuickBooksId,
                    isCertified =
                        false,
                    cancellationReason =
                        storedInvoice.CancellationReason,
                    cancellationDate =
                        storedInvoice.CancellationDate,
                    cancellationAuthorizationNumber =
                        ""
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        private async Task<string>
            ObtenerDocumentoQuickBooksAsync(
                string id)
        {
            string salesReceiptJson =
                await _quickBooksService
                    .GetSalesReceiptById(
                        id
                    );

            if (ContainsDocument(
                salesReceiptJson,
                "SalesReceipt"))
            {
                return salesReceiptJson;
            }

            string invoiceJson =
                await _quickBooksService
                    .GetInvoiceById(
                        id
                    );

            if (ContainsDocument(
                invoiceJson,
                "Invoice"))
            {
                return invoiceJson;
            }

            return "";
        }

        private static QuickBooksDocumentSummary
            ParseQuickBooksDocument(
                string json)
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    json
                );

            JsonElement queryResponse =
                document.RootElement
                    .GetProperty(
                        "QueryResponse"
                    );

            JsonElement qbDocument;
            string saleType;

            if (queryResponse.TryGetProperty(
                "SalesReceipt",
                out JsonElement receipts))
            {
                qbDocument =
                    receipts[0];

                saleType =
                    "contado";
            }
            else if (queryResponse.TryGetProperty(
                "Invoice",
                out JsonElement invoices))
            {
                qbDocument =
                    invoices[0];

                saleType =
                    "credito";
            }
            else
            {
                throw new Exception(
                    "QuickBooks no devolvió un documento válido."
                );
            }

            string customerName =
                "Consumidor Final";

            if (qbDocument.TryGetProperty(
                    "CustomerRef",
                    out JsonElement customerRef) &&
                customerRef.TryGetProperty(
                    "name",
                    out JsonElement customerNameElement))
            {
                customerName =
                    customerNameElement.GetString()
                    ?? "Consumidor Final";
            }

            string invoiceNumber =
                GetString(
                    qbDocument,
                    "DocNumber"
                );

            decimal total =
                GetDecimal(
                    qbDocument,
                    "TotalAmt"
                );

            DateTime issueDate =
                DateTime.UtcNow;

            string txnDate =
                GetString(
                    qbDocument,
                    "TxnDate"
                );

            if (DateTime.TryParse(
                txnDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsedDate))
            {
                issueDate =
                    DateTime.SpecifyKind(
                        parsedDate.Date,
                        DateTimeKind.Utc
                    );
            }

            return new QuickBooksDocumentSummary
            {
                InvoiceNumber =
                    invoiceNumber,

                CustomerName =
                    string.IsNullOrWhiteSpace(
                        customerName)
                        ? "Consumidor Final"
                        : customerName.Trim(),

                IssueDate =
                    issueDate,

                Total =
                    total,

                SaleType =
                    saleType
            };
        }

        private static bool ContainsDocument(
            string? json,
            string property)
        {
            return
                !string.IsNullOrWhiteSpace(
                    json
                ) &&
                json.Contains(
                    $"\"{property}\"",
                    StringComparison.Ordinal
                );
        }

        private static string GetString(
            JsonElement element,
            string property)
        {
            if (!element.TryGetProperty(
                    property,
                    out JsonElement value))
            {
                return "";
            }

            return value.ValueKind ==
                JsonValueKind.String
                    ? value.GetString() ?? ""
                    : value.ToString();
        }

        private static decimal GetDecimal(
            JsonElement element,
            string property)
        {
            if (!element.TryGetProperty(
                    property,
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

            return decimal.TryParse(
                value.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimal parsed)
                    ? parsed
                    : 0m;
        }

        private sealed class QuickBooksDocumentSummary
        {
            public string InvoiceNumber { get; set; } = "";

            public string CustomerName { get; set; } = "";

            public DateTime IssueDate { get; set; }

            public decimal Total { get; set; }

            public string SaleType { get; set; } = "contado";
        }
    }
}