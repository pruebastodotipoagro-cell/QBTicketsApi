using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/customers")]
    public class CustomersController : ControllerBase
    {
        [HttpGet("nit-test")]
        public IActionResult NitTest([FromServices] CustomerLookupService lookup)
        {
            return Ok(new
            {
                customer = "ALEJANDRO REYES, PEDRO",
                nit = lookup.GetNit("ALEJANDRO REYES, PEDRO")
            });
        }
    }
}