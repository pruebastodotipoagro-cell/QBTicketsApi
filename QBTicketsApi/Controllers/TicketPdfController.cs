using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.DTOs;
using QBTicketsApi.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
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
            [FromQuery] string? customerName = null,
            [FromQuery] bool certifyFel = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "El ID del documento es obligatorio."
                    });
                }

                string json =
                    await ObtenerDocumentoQuickBooksAsync(id);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return NotFound(new
                    {
                        success = false,
                        error = "No se encontró el recibo o factura."
                    });
                }

                string saleType =
                    EsReciboVenta(json)
                        ? "contado"
                        : "credito";

                string nitFinal =
                    LimpiarNit(nit);

                string nombreSolicitado =
                    string.IsNullOrWhiteSpace(customerName)
                        ? "Consumidor Final"
                        : customerName.Trim();

                /*
                 * IMPORTANTE:
                 * CF no obliga a usar "Consumidor Final".
                 * Se conserva el nombre escrito o seleccionado.
                 */
                if (certifyFel)
                {
                    string? fiscalError =
                        ValidarDatosFiscalesParaCertificar(
                            nitFinal,
                            nombreSolicitado
                        );

                    if (!string.IsNullOrWhiteSpace(fiscalError))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error = fiscalError
                        });
                    }
                }

                if (!certifyFel)
                {
                    byte[] recibo =
                        _ticketPdfService
                            .GenerateUncertifiedReceiptPdf(
                                json,
                                saleType,
                                nitFinal,
                                nombreSolicitado,
                                Array.Empty<ItemDiscountRequest>()
                            );

                    return File(
                        recibo,
                        "application/pdf",
                        $"recibo-{id}-no-certificado.pdf"
                    );
                }

                FelResult fel =
                    await _felService.CertifyAsync(
                        id,
                        json,
                        saleType,
                        nitFinal,
                        nombreSolicitado,
                        Array.Empty<ItemDiscountRequest>()
                    );

                /*
                 * Para la impresión usamos siempre el nombre solicitado
                 * por el cajero. Esto también corrige reimpresiones de
                 * documentos ya guardados anteriormente como CF.
                 */
                string nombreFinal =
                    string.IsNullOrWhiteSpace(nombreSolicitado)
                        ? (
                            string.IsNullOrWhiteSpace(fel.CustomerName)
                                ? "Consumidor Final"
                                : fel.CustomerName.Trim()
                          )
                        : nombreSolicitado;

                byte[] pdf =
                    _ticketPdfService.GenerateSalesReceiptPdf(
                        json,
                        fel,
                        saleType,
                        nombreFinal,
                        Array.Empty<ItemDiscountRequest>()
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

        [HttpPost("{id}/pdf-with-discounts")]
        public async Task<IActionResult> GetTicketPdfWithDiscounts(
            string id,
            [FromBody] DiscountedTicketRequest? request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "El ID del documento es obligatorio."
                    });
                }

                if (request is null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "La solicitud está vacía."
                    });
                }

                List<ItemDiscountRequest> discounts =
                    request.Discounts ??
                    new List<ItemDiscountRequest>();

                string? errorDescuento =
                    ValidarDescuentos(discounts);

                if (!string.IsNullOrWhiteSpace(errorDescuento))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = errorDescuento
                    });
                }

                string json =
                    await ObtenerDocumentoQuickBooksAsync(id);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return NotFound(new
                    {
                        success = false,
                        error = "No se encontró el recibo o factura."
                    });
                }

                string saleType =
                    EsReciboVenta(json)
                        ? "contado"
                        : "credito";

                string nitFinal =
                    LimpiarNit(request.Nit);

                string nombreSolicitado =
                    string.IsNullOrWhiteSpace(request.CustomerName)
                        ? "Consumidor Final"
                        : request.CustomerName.Trim();

                if (request.CertifyFel)
                {
                    string? fiscalError =
                        ValidarDatosFiscalesParaCertificar(
                            nitFinal,
                            nombreSolicitado
                        );

                    if (!string.IsNullOrWhiteSpace(fiscalError))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error = fiscalError
                        });
                    }
                }

                if (!request.CertifyFel)
                {
                    byte[] recibo =
                        _ticketPdfService
                            .GenerateUncertifiedReceiptPdf(
                                json,
                                saleType,
                                nitFinal,
                                nombreSolicitado,
                                discounts
                            );

                    return File(
                        recibo,
                        "application/pdf",
                        $"recibo-{id}-no-certificado.pdf"
                    );
                }

                FelResult fel =
                    await _felService.CertifyAsync(
                        id,
                        json,
                        saleType,
                        nitFinal,
                        nombreSolicitado,
                        discounts
                    );

                string nombreFinal =
                    string.IsNullOrWhiteSpace(nombreSolicitado)
                        ? (
                            string.IsNullOrWhiteSpace(fel.CustomerName)
                                ? "Consumidor Final"
                                : fel.CustomerName.Trim()
                          )
                        : nombreSolicitado;

                byte[] pdf =
                    _ticketPdfService.GenerateSalesReceiptPdf(
                        json,
                        fel,
                        saleType,
                        nombreFinal,
                        discounts
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

        private async Task<string> ObtenerDocumentoQuickBooksAsync(
            string id)
        {
            string salesReceiptJson =
                await _quickBooksService.GetSalesReceiptById(id);

            if (EsReciboVenta(salesReceiptJson))
            {
                return salesReceiptJson;
            }

            string invoiceJson =
                await _quickBooksService.GetInvoiceById(id);

            if (EsFacturaCredito(invoiceJson))
            {
                return invoiceJson;
            }

            return string.Empty;
        }

        private static bool EsReciboVenta(string? json)
        {
            return !string.IsNullOrWhiteSpace(json) &&
                   json.Contains(
                       "\"SalesReceipt\"",
                       StringComparison.Ordinal
                   );
        }

        private static bool EsFacturaCredito(string? json)
        {
            return !string.IsNullOrWhiteSpace(json) &&
                   json.Contains(
                       "\"Invoice\"",
                       StringComparison.Ordinal
                   );
        }

        private static string LimpiarNit(string? nit)
        {
            if (string.IsNullOrWhiteSpace(nit))
            {
                return "CF";
            }

            string nitLimpio =
                nit.Trim().Replace("-", "");

            return string.IsNullOrWhiteSpace(nitLimpio)
                ? "CF"
                : nitLimpio;
        }

        private static string?
            ValidarDatosFiscalesParaCertificar(
                string nit,
                string? customerName)
        {
            /*
             * CF puede llevar cualquier nombre no vacío.
             */
            if (nit.Equals(
                "CF",
                StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(customerName))
            {
                return
                    "Debe verificar el NIT antes de certificar.";
            }

            if (customerName.Trim().Equals(
                "Consumidor Final",
                StringComparison.OrdinalIgnoreCase))
            {
                return
                    "El nombre fiscal no corresponde al NIT indicado.";
            }

            return null;
        }

        private static string? ValidarDescuentos(
            IEnumerable<ItemDiscountRequest> discounts)
        {
            var lineasEncontradas =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (ItemDiscountRequest discount in discounts)
            {
                string lineId =
                    discount.LineId?.Trim() ??
                    string.Empty;

                if (string.IsNullOrWhiteSpace(lineId))
                {
                    return
                        "Todos los descuentos deben indicar el LineId.";
                }

                if (discount.Amount < 0)
                {
                    return
                        $"El descuento de la línea {lineId} " +
                        "no puede ser negativo.";
                }

                if (!lineasEncontradas.Add(lineId))
                {
                    return
                        $"La línea {lineId} está repetida " +
                        "en la lista de descuentos.";
                }
            }

            return null;
        }


    }
}