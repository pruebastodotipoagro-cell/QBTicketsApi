using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/invoices")]
    public class TicketPdfController : ControllerBase
    {
        private readonly QuickBooksService _quickBooksService;
        private readonly TicketPdfService _ticketPdfService;
        private readonly FelService _felService;

        public TicketPdfController(
            QuickBooksService quickBooksService,
            TicketPdfService ticketPdfService,
            FelService felService)
        {
            _quickBooksService = quickBooksService;
            _ticketPdfService = ticketPdfService;
            _felService = felService;
        }

        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GetTicketPdf(
    string id,
    [FromQuery] string? nit = null,
    [FromQuery] decimal descuento = 0)
        {
            var json = await _quickBooksService.GetSalesReceiptById(id);

            if (string.IsNullOrWhiteSpace(json) || !json.Contains("SalesReceipt"))
            {
                json = await _quickBooksService.GetInvoiceById(id);
            }

            if (string.IsNullOrWhiteSpace(json))
                return NotFound("No se encontró el recibo.");

            if (descuento < 0)
                return BadRequest("El descuento no puede ser negativo.");

            string saleType = json.Contains("SalesReceipt")
     ? "contado"
     : "credito";

            var fel = await _felService.CertifyAsync(
                id,
                json,
                saleType,
                nit,
                descuento
            );

            var pdf = _ticketPdfService.GenerateSalesReceiptPdf(
                json,
                fel,
                saleType
            );

            return File(
                pdf,
                "application/pdf",
                $"ticket-{id}.pdf"
            );
        }
    }
}