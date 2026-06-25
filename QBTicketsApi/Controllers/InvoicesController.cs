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
    }
}