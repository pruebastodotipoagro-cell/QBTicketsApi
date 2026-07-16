using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;
using System.Text.Json;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/quickbooks-customers")]
    public class QuickBooksCustomersController
        : ControllerBase
    {
        private readonly QuickBooksService
            _quickBooksService;

        public QuickBooksCustomersController(
            QuickBooksService quickBooksService)
        {
            _quickBooksService =
                quickBooksService;
        }

        [HttpGet("{customerId}/raw")]
        public async Task<IActionResult>
            GetCustomerRaw(string customerId)
        {
            try
            {
                string json =
                    await _quickBooksService
                        .GetCustomerByIdAsync(
                            customerId
                        );

                return Content(
                    json,
                    "application/json"
                );
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
        [HttpGet("from-document/{documentId}/raw")]
        public async Task<IActionResult> GetCustomerFromDocumentRaw(
    string documentId)
        {
            try
            {
                string documentJson =
                    await _quickBooksService
                        .GetSalesReceiptById(documentId);

                if (string.IsNullOrWhiteSpace(documentJson) ||
                    !documentJson.Contains(
                        "\"SalesReceipt\"",
                        StringComparison.Ordinal))
                {
                    documentJson =
                        await _quickBooksService
                            .GetInvoiceById(documentId);
                }

                if (string.IsNullOrWhiteSpace(documentJson))
                {
                    return NotFound(new
                    {
                        success = false,
                        error =
                            "No se encontró la factura o recibo en QuickBooks."
                    });
                }

                using JsonDocument document =
                    JsonDocument.Parse(documentJson);

                if (!document.RootElement.TryGetProperty(
                        "QueryResponse",
                        out JsonElement queryResponse))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "QuickBooks no devolvió QueryResponse."
                    });
                }

                JsonElement qbDocument;

                if (queryResponse.TryGetProperty(
                        "SalesReceipt",
                        out JsonElement receipts) &&
                    receipts.GetArrayLength() > 0)
                {
                    qbDocument = receipts[0];
                }
                else if (queryResponse.TryGetProperty(
                             "Invoice",
                             out JsonElement invoices) &&
                         invoices.GetArrayLength() > 0)
                {
                    qbDocument = invoices[0];
                }
                else
                {
                    return NotFound(new
                    {
                        success = false,
                        error =
                            "No se encontró Invoice ni SalesReceipt."
                    });
                }

                if (!qbDocument.TryGetProperty(
                        "CustomerRef",
                        out JsonElement customerRef))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "La transacción no contiene CustomerRef."
                    });
                }

                string customerId =
                    customerRef.TryGetProperty(
                        "value",
                        out JsonElement customerIdElement)
                        ? customerIdElement.GetString() ?? ""
                        : "";

                if (string.IsNullOrWhiteSpace(customerId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "No se encontró el ID interno del cliente."
                    });
                }

                string customerJson =
                    await _quickBooksService
                        .GetCustomerByIdAsync(customerId);

                return Content(
                    customerJson,
                    "application/json"
                );
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

    }
}
