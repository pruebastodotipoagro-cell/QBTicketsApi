using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/fel")]
    public class FelController : ControllerBase
    {
        private readonly MegaprintService _megaprintService;
        private readonly QuickBooksService _quickBooksService;
        private readonly FelXmlBuilderService _xmlBuilder;
        private readonly FelService _felService;

        public FelController(
            MegaprintService megaprintService,
            QuickBooksService quickBooksService,
            FelXmlBuilderService xmlBuilder,
            FelService felService)
        {
            _megaprintService = megaprintService;
            _quickBooksService = quickBooksService;
            _xmlBuilder = xmlBuilder;
            _felService = felService;
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

        // GET /api/fel/xml-preview/{id}
        [HttpGet("xml-preview/{id}")]
        public async Task<IActionResult> XmlPreview(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { success = false, error = "id es requerido" });

            var json = await _quickBooksService.GetSalesReceiptById(id);

            if (string.IsNullOrWhiteSpace(json) || !json.Contains("SalesReceipt"))
            {
                json = await _quickBooksService.GetInvoiceById(id);
            }

            if (string.IsNullOrWhiteSpace(json))
                return NotFound(new { success = false, error = "No se encontró el documento en QuickBooks." });

            try
            {
                var xml = _xmlBuilder.BuildFactXml(json);
                return Content(xml, "application/xml");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

       
    }
}