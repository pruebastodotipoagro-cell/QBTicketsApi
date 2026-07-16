using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

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
    }
}