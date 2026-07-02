using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/customers")]
    public class CustomersController : ControllerBase
    {
        private readonly CustomerLookupService _customerLookupService;

        public CustomersController(CustomerLookupService customerLookupService)
        {
            _customerLookupService = customerLookupService;
        }

        [HttpGet]
        public IActionResult GetCustomers()
        {
            var customers = _customerLookupService.GetCustomerNames();

            return Ok(new
            {
                customers = customers
            });
        }
    }
}