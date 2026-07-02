using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

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
            var result = await _quickBooksService.GetSalesReceipts();
            return Content(result, "application/json");
        }

        [HttpGet("sales-receipts")]
        public async Task<IActionResult> GetSalesReceipts()
        {
            var result = await _quickBooksService.GetSalesReceipts();
            return Content(result, "application/json");
        }

        [HttpGet("credit-invoices")]
        public async Task<IActionResult> GetCreditInvoices()
        {
            var result = await _quickBooksService.GetCreditInvoicesList();
            return Ok(new { invoices = result });
        }

        [HttpGet("credit-summary")]
        public async Task<IActionResult> GetCreditSummary()
        {
            var result = await _quickBooksService.GetCreditSummaryList();
            return Ok(new { customers = result });
        }
    }
}