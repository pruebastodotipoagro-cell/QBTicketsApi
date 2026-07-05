using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/fel")]
    public class FelController : ControllerBase
    {
        private readonly MegaprintService _megaprintService;

        public FelController(MegaprintService megaprintService)
        {
            _megaprintService = megaprintService;
        }

        [HttpGet("token-test")]
        public async Task<IActionResult> TokenTest()
        {
            var token = await _megaprintService.SolicitarTokenAsync();

            return Ok(new
            {
                success = true,
                tokenStart = token.Substring(0, Math.Min(30, token.Length))
            });
        }
    }
}