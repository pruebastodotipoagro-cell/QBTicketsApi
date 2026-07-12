using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;
using System.Globalization;
using System.Xml.Linq;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/invoices")]
    public class InvoicesController : ControllerBase
    {
        private readonly QuickBooksService _quickBooksService;

        public InvoicesController(QuickBooksService quickBooksService)
        {
            _quickBooksService = quickBooksService;
        }

        [HttpGet("quickbooks-test")]
        public async Task<IActionResult> QuickBooksTest()
        {
            string rawResult = await _quickBooksService.GetSalesReceipts();

            return Content(rawResult, "application/xml");
        }

        // GET /api/invoices/sales-receipts?desde=2026-07-01&hasta=2026-07-08
        [HttpGet("sales-receipts")]
        public async Task<IActionResult> GetSalesReceipts(
            [FromQuery] string? desde = null,
            [FromQuery] string? hasta = null)
        {
            try
            {
                string rawResult =
                    await _quickBooksService.GetSalesReceipts(desde, hasta);

                if (string.IsNullOrWhiteSpace(rawResult))
                {
                    return Ok(new
                    {
                        invoices = Array.Empty<object>()
                    });
                }

                XDocument document = XDocument.Parse(rawResult);

                XNamespace ns =
                    document.Root?.GetDefaultNamespace() ?? XNamespace.None;

                XElement? fault = document
                    .Descendants(ns + "Fault")
                    .FirstOrDefault();

                if (fault != null)
                {
                    string errorMessage =
                        fault.Descendants(ns + "Message")
                             .FirstOrDefault()?.Value
                        ?? "QuickBooks devolvió un error.";

                    string errorDetail =
                        fault.Descendants(ns + "Detail")
                             .FirstOrDefault()?.Value
                        ?? "";

                    return StatusCode(502, new
                    {
                        error = errorMessage,
                        detail = errorDetail
                    });
                }

                var invoices = document
                    .Descendants(ns + "SalesReceipt")
                    .Select(receipt =>
                    {
                        XElement? customerRef =
                            receipt.Element(ns + "CustomerRef");

                        string qbInvoiceId =
                            receipt.Element(ns + "Id")?.Value ?? "";

                        string invoiceNumber =
                            receipt.Element(ns + "DocNumber")?.Value
                            ?? qbInvoiceId;

                        string customerName =
                            customerRef?.Attribute("name")?.Value
                            ?? customerRef?.Value
                            ?? "Consumidor Final";

                        string issueDateText =
                            receipt.Element(ns + "TxnDate")?.Value ?? "";

                        DateTime issueDate;

                        if (!DateTime.TryParseExact(
                            issueDateText,
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out issueDate))
                        {
                            issueDate = DateTime.Today;
                        }

                        string totalText =
                            receipt.Element(ns + "TotalAmt")?.Value ?? "0";

                        decimal total;

                        if (!decimal.TryParse(
                            totalText,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out total))
                        {
                            total = 0;
                        }

                        return new
                        {
                            qbInvoiceId = qbInvoiceId,
                            invoiceNumber = invoiceNumber,
                            customerName = customerName,
                            customerNit = "CF",
                            issueDate = issueDate,
                            total = total,
                            saleType = "contado",
                            balance = 0m
                        };
                    })
                    .OrderByDescending(x => x.issueDate)
                    .ToList();

                return Ok(new
                {
                    invoices = invoices
                });
            }
            catch (System.Xml.XmlException ex)
            {
                return StatusCode(500, new
                {
                    error = "QuickBooks devolvió una respuesta XML inválida.",
                    detail = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "No se pudieron cargar los recibos de venta.",
                    detail = ex.Message
                });
            }
        }

        // GET /api/invoices/credit-invoices?desde=2026-07-01&hasta=2026-07-08
        [HttpGet("credit-invoices")]
        public async Task<IActionResult> GetCreditInvoices(
            [FromQuery] string? desde = null,
            [FromQuery] string? hasta = null)
        {
            var result =
                await _quickBooksService.GetCreditInvoicesList(desde, hasta);

            return Ok(new
            {
                invoices = result
            });
        }

        [HttpGet("credit-summary")]
        public async Task<IActionResult> GetCreditSummary()
        {
            var result =
                await _quickBooksService.GetCreditSummaryList();

            return Ok(new
            {
                customers = result
            });
        }

        // GET /api/invoices/{id}/items
        [HttpGet("{id}/items")]
        public async Task<IActionResult> GetDocumentItems(string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "El ID del documento es obligatorio."
                    });
                }

                var result =
                    await _quickBooksService
                        .GetDocumentItemsAsync(id);

                return Ok(result);
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