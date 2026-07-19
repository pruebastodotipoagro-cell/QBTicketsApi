using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.DTOs;
using QBTicketsApi.Services;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
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
                List<InvoiceResponseDto> invoices =
                    await _quickBooksService
                        .GetSalesReceiptsList(
                            desde,
                            hasta
                        );

                return Ok(new
                {
                    invoices
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

        // GET /api/invoices/raw-sales-receipt/11724
        [HttpGet("raw-sales-receipt/{id}")]
        public async Task<IActionResult> GetRawSalesReceipt(string id)
        {
            try
            {
                string json =
                    await _quickBooksService
                        .GetSalesReceiptById(id);

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
    }
}