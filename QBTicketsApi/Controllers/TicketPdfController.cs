using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.DTOs;
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

        // Impresión normal, sin descuentos por producto.
        // GET /api/invoices/{id}/pdf?nit=CF
        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GetTicketPdf(
            string id,
            [FromQuery] string? nit = null)
        {
            try
            {
                string json = await ObtenerDocumentoQuickBooks(id);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return NotFound(new
                    {
                        success = false,
                        error = "No se encontró el recibo o factura."
                    });
                }

                string saleType = json.Contains("\"SalesReceipt\"")
                    ? "contado"
                    : "credito";

                var fel = await _felService.CertifyAsync(
                    id,
                    json,
                    saleType,
                    nit,
                    0m
                );

                byte[] pdf =
                    _ticketPdfService.GenerateSalesReceiptPdf(
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
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        // Impresión y certificación con descuentos por producto.
        // POST /api/invoices/{id}/pdf-with-discounts
        [HttpPost("{id}/pdf-with-discounts")]
        public async Task<IActionResult> GetTicketPdfWithDiscounts(
            string id,
            [FromBody] DiscountedTicketRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "La solicitud está vacía."
                    });
                }

                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "El ID del documento es obligatorio."
                    });
                }

                if (request.Discounts == null)
                {
                    request.Discounts =
                        new List<ItemDiscountRequest>();
                }

                foreach (var discount in request.Discounts)
                {
                    if (discount == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(discount.LineId))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error = "Todos los descuentos deben indicar el LineId."
                        });
                    }

                    if (discount.Amount < 0)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error =
                                $"El descuento de la línea {discount.LineId} " +
                                "no puede ser negativo."
                        });
                    }
                }

                string json = await ObtenerDocumentoQuickBooks(id);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return NotFound(new
                    {
                        success = false,
                        error = "No se encontró el recibo o factura."
                    });
                }

                string saleType = json.Contains("\"SalesReceipt\"")
                    ? "contado"
                    : "credito";

                string? nitFinal = string.IsNullOrWhiteSpace(request.Nit)
                    ? "CF"
                    : request.Nit.Trim().Replace("-", "");

                string? nombreFiscal =
                    string.IsNullOrWhiteSpace(request.CustomerName)
                        ? null
                        : request.CustomerName.Trim();

                var fel = await _felService.CertifyAsync(
                    id,
                    json,
                    saleType,
                    nitFinal,
                    nombreFiscal,
                    request.Discounts
                );

                byte[] pdf =
                    _ticketPdfService.GenerateSalesReceiptPdf(
                        json,
                        fel,
                        saleType,
                        nombreFiscal,
                        request.Discounts
                    );

                return File(
                    pdf,
                    "application/pdf",
                    $"ticket-{id}-descuento.pdf"
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

        private async Task<string> ObtenerDocumentoQuickBooks(string id)
        {
            string json =
                await _quickBooksService.GetSalesReceiptById(id);

            if (string.IsNullOrWhiteSpace(json) ||
                !json.Contains("\"SalesReceipt\""))
            {
                json =
                    await _quickBooksService.GetInvoiceById(id);
            }

            if (string.IsNullOrWhiteSpace(json) ||
                (!json.Contains("\"SalesReceipt\"") &&
                 !json.Contains("\"Invoice\"")))
            {
                return "";
            }

            return json;
        }
    }
}