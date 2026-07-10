using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/customers")]
    public class CustomersController : ControllerBase
    {
        private readonly CustomerLookupService _customerLookupService;
        private readonly MegaprintService _megaprintService;

        public CustomersController(
            CustomerLookupService customerLookupService,
            MegaprintService megaprintService)
        {
            _customerLookupService = customerLookupService;
            _megaprintService = megaprintService;
        }

        // GET /api/customers
        [HttpGet]
        public IActionResult GetCustomers()
        {
            var customers = _customerLookupService.GetCustomerNames();

            return Ok(new
            {
                customers = customers
            });
        }

        // GET /api/customers/verify-nit?nit=120074427
        [HttpGet("verify-nit")]
        public async Task<IActionResult> VerifyNit(
            [FromQuery] string? nit)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nit))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Debe ingresar un NIT."
                    });
                }

                string nitLimpio = nit.Trim().Replace("-", "");

                if (nitLimpio.Equals(
                    "CF",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(new
                    {
                        success = true,
                        nit = "CF",
                        name = "Consumidor Final"
                    });
                }

                string nombre =
                    await _megaprintService
                        .RetornarNombreClienteAsync(nitLimpio);

                return Ok(new
                {
                    success = true,
                    nit = nitLimpio,
                    name = nombre
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
    }
}