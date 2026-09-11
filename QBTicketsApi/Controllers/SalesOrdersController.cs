using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/sales-orders")]
    public class SalesOrdersController : ControllerBase
    {
        private readonly QuickBooksService
            _quickBooksService;

        public SalesOrdersController(
            QuickBooksService quickBooksService)
        {
            _quickBooksService =
                quickBooksService;
        }

        [HttpGet("test")]
        public async Task<IActionResult>
            TestSalesOrders()
        {
            try
            {
                string json =
                    await _quickBooksService
                        .GetSalesOrdersTestAsync();

                return Content(
                    json,
                    "application/json"
                );
            }
            catch (Exception ex)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        error = ex.Message
                    }
                );
            }
        }
    }
}