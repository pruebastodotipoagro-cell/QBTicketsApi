using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.Models;
using QBTicketsApi.Services;

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
        private readonly AppDbContext _db;

        public TicketPdfController(
            QuickBooksService quickBooksService,
            TicketPdfService ticketPdfService,
            FelService felService,
            AppDbContext db)
        {
            _quickBooksService =
                quickBooksService;

            _ticketPdfService =
                ticketPdfService;

            _felService =
                felService;

            _db =
                db;
        }

        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GetTicketPdf(
            string id,
            [FromQuery] string? nit = null,
            [FromQuery] string? customerName = null,
            [FromQuery] bool certifyFel = true,
            [FromQuery] string? priceType = null,
            [FromQuery] decimal creditPercentage = 0m)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "El ID del documento es obligatorio."
                    });
                }

                id =
                    id.Trim();

                Invoice? storedInvoice =
                    await ObtenerFacturaGuardadaAsync(
                        id
                    );

                if (storedInvoice != null &&
                    storedInvoice.IsCancelled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "La factura está anulada y no puede imprimirse como vigente."
                    });
                }

                string json =
                    await ObtenerDocumentoQuickBooksAsync(
                        id
                    );

                if (string.IsNullOrWhiteSpace(json))
                {
                    return NotFound(new
                    {
                        success = false,
                        error =
                            "No se encontró el recibo o factura en QuickBooks."
                    });
                }

                string saleType =
                    EsReciboVenta(json)
                        ? "contado"
                        : "credito";

                List<ItemDiscountRequest> discounts =
                    ObtenerDescuentosGuardados(
                        storedInvoice
                    );

                string nitFinal;

                string nombreFinal;

                /*
                 * Si la factura ya fue certificada,
                 * deben utilizarse los datos fiscales
                 * originales guardados.
                 */
                if (storedInvoice != null &&
                    storedInvoice.IsCertified)
                {
                    nitFinal =
                        string.IsNullOrWhiteSpace(
                            storedInvoice.CustomerNit)
                            ? "CF"
                            : storedInvoice.CustomerNit
                                .Trim();

                    nombreFinal =
                        string.IsNullOrWhiteSpace(
                            storedInvoice.CustomerName)
                            ? "Consumidor Final"
                            : storedInvoice.CustomerName
                                .Trim();
                }
                else
                {
                    nitFinal =
                        LimpiarNit(
                            nit
                        );

                    nombreFinal =
                        string.IsNullOrWhiteSpace(
                            customerName)
                            ? "Consumidor Final"
                            : customerName.Trim();
                }

                string finalPriceType =
                    ObtenerTipoPrecio(
                        priceType,
                        saleType,
                        storedInvoice
                    );

                decimal finalCreditPercentage =
                    ObtenerPorcentajeCredito(
                        finalPriceType,
                        creditPercentage,
                        storedInvoice
                    );

                if (storedInvoice == null || !storedInvoice.IsCertified)
                {
                    var sync = await _quickBooksService.SynchronizeDashboardDocumentAsync(
                        id, finalPriceType, finalCreditPercentage, discounts);
                    json = sync.DocumentJson;
                    storedInvoice = await ObtenerFacturaGuardadaAsync(id);
                }

                if (certifyFel)
                {
                    string? fiscalError =
                        ValidarDatosFiscalesParaCertificar(
                            nitFinal,
                            nombreFinal
                        );

                    if (!string.IsNullOrWhiteSpace(
                        fiscalError))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error =
                                fiscalError
                        });
                    }
                }

                if (!certifyFel)
                {
                    byte[] receipt =
                        _ticketPdfService
                            .GenerateUncertifiedReceiptPdf(
                                json,
                                saleType,
                                nitFinal,
                                nombreFinal,
                                discounts,
                                finalPriceType,
                                finalCreditPercentage
                            );

                    return File(
                        receipt,
                        "application/pdf",
                        $"recibo-{id}-no-certificado.pdf"
                    );
                }

                FelResult fel =
                    await _felService
                        .CertifyAsync(
                            id,
                            json,
                            saleType,
                            nitFinal,
                            nombreFinal,
                            discounts,
                            finalPriceType,
                            finalCreditPercentage
                        );

                /*
                 * Después de certificar se conservan
                 * exactamente los datos devueltos por FEL.
                 */
                string printedName =
                    string.IsNullOrWhiteSpace(
                        fel.CustomerName)
                        ? nombreFinal
                        : fel.CustomerName.Trim();

                byte[] pdf =
                    _ticketPdfService
                        .GenerateSalesReceiptPdf(
                            json,
                            fel,
                            saleType,
                            printedName,
                            discounts,
                            finalPriceType,
                            finalCreditPercentage
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
                    error =
                        ex.Message
                });
            }
        }

        [HttpPost("{id}/pdf-with-discounts")]
        public async Task<IActionResult>
            GetTicketPdfWithDiscounts(
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
                        error =
                            "El ID del documento es obligatorio."
                    });
                }

                if (request == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "La solicitud está vacía."
                    });
                }

                id =
                    id.Trim();

                Invoice? storedInvoice =
                    await ObtenerFacturaGuardadaAsync(
                        id
                    );

                if (storedInvoice != null &&
                    storedInvoice.IsCancelled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "La factura está anulada y no puede imprimirse como vigente."
                    });
                }

                /*
                 * Una factura certificada no puede recibir
                 * descuentos distintos ni cambiar el precio.
                 * Se usan los valores ya guardados.
                 */
                bool alreadyCertified =
                    storedInvoice != null &&
                    storedInvoice.IsCertified;

                List<ItemDiscountRequest> discounts =
                    alreadyCertified
                        ? ObtenerDescuentosGuardados(
                            storedInvoice
                          )
                        : request.Discounts ??
                          new List<ItemDiscountRequest>();

                string? discountError =
                    ValidarDescuentos(
                        discounts
                    );

                if (!string.IsNullOrWhiteSpace(
                    discountError))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            discountError
                    });
                }

                string json =
                    await ObtenerDocumentoQuickBooksAsync(
                        id
                    );

                if (string.IsNullOrWhiteSpace(json))
                {
                    return NotFound(new
                    {
                        success = false,
                        error =
                            "No se encontró el recibo o factura en QuickBooks."
                    });
                }

                string saleType =
                    EsReciboVenta(json)
                        ? "contado"
                        : "credito";

                string nitFinal;

                string nombreFinal;

                if (alreadyCertified)
                {
                    nitFinal =
                        string.IsNullOrWhiteSpace(
                            storedInvoice!.CustomerNit)
                            ? "CF"
                            : storedInvoice.CustomerNit
                                .Trim();

                    nombreFinal =
                        string.IsNullOrWhiteSpace(
                            storedInvoice.CustomerName)
                            ? "Consumidor Final"
                            : storedInvoice.CustomerName
                                .Trim();
                }
                else
                {
                    nitFinal =
                        LimpiarNit(
                            request.Nit
                        );

                    nombreFinal =
                        string.IsNullOrWhiteSpace(
                            request.CustomerName)
                            ? "Consumidor Final"
                            : request.CustomerName
                                .Trim();
                }

                string finalPriceType =
                    ObtenerTipoPrecio(
                        request.PriceType,
                        saleType,
                        storedInvoice
                    );

                decimal finalCreditPercentage =
                    ObtenerPorcentajeCredito(
                        finalPriceType,
                        request.CreditPercentage,
                        storedInvoice
                    );

                if (!alreadyCertified)
                {
                    var sync = await _quickBooksService.SynchronizeDashboardDocumentAsync(
                        id, finalPriceType, finalCreditPercentage, discounts);
                    json = sync.DocumentJson;
                    storedInvoice = await ObtenerFacturaGuardadaAsync(id);
                }

                if (request.CertifyFel)
                {
                    string? fiscalError =
                        ValidarDatosFiscalesParaCertificar(
                            nitFinal,
                            nombreFinal
                        );

                    if (!string.IsNullOrWhiteSpace(
                        fiscalError))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error =
                                fiscalError
                        });
                    }
                }

                if (!request.CertifyFel)
                {
                    byte[] receipt =
                        _ticketPdfService
                            .GenerateUncertifiedReceiptPdf(
                                json,
                                saleType,
                                nitFinal,
                                nombreFinal,
                                discounts,
                                finalPriceType,
                                finalCreditPercentage
                            );

                    return File(
                        receipt,
                        "application/pdf",
                        $"recibo-{id}-no-certificado.pdf"
                    );
                }

                FelResult fel =
                    await _felService
                        .CertifyAsync(
                            id,
                            json,
                            saleType,
                            nitFinal,
                            nombreFinal,
                            discounts,
                            finalPriceType,
                            finalCreditPercentage
                        );

                string printedName =
                    string.IsNullOrWhiteSpace(
                        fel.CustomerName)
                        ? nombreFinal
                        : fel.CustomerName.Trim();

                byte[] pdf =
                    _ticketPdfService
                        .GenerateSalesReceiptPdf(
                            json,
                            fel,
                            saleType,
                            printedName,
                            discounts,
                            finalPriceType,
                            finalCreditPercentage
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
                    error =
                        ex.Message
                });
            }
        }

        private async Task<Invoice?>
            ObtenerFacturaGuardadaAsync(
                string quickBooksId)
        {
            return await _db.Invoices
                .AsNoTracking()
                .Include(x =>
                    x.Lines
                )
                .Where(x =>
                    x.QuickBooksId ==
                        quickBooksId
                )
                .OrderByDescending(x =>
                    x.CreatedAt
                )
                .FirstOrDefaultAsync();
        }

        private static List<ItemDiscountRequest>
            ObtenerDescuentosGuardados(
                Invoice? invoice)
        {
            if (invoice == null ||
                invoice.Lines == null ||
                invoice.Lines.Count == 0)
            {
                return new List<ItemDiscountRequest>();
            }

            return invoice.Lines
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.QuickBooksLineId
                    ) &&
                    x.DiscountAmount > 0m
                )
                .Select(x =>
                    new ItemDiscountRequest
                    {
                        LineId =
                            x.QuickBooksLineId,

                        Amount =
                            x.DiscountAmount
                    }
                )
                .ToList();
        }

        private async Task<string>
            ObtenerDocumentoQuickBooksAsync(
                string id)
        {
            string salesReceiptJson =
                await _quickBooksService
                    .GetSalesReceiptById(
                        id
                    );

            if (EsReciboVenta(
                salesReceiptJson))
            {
                return salesReceiptJson;
            }

            string invoiceJson =
                await _quickBooksService
                    .GetInvoiceById(
                        id
                    );

            if (EsFacturaCredito(
                invoiceJson))
            {
                return invoiceJson;
            }

            return "";
        }

        private static bool EsReciboVenta(
            string? json)
        {
            return
                !string.IsNullOrWhiteSpace(
                    json
                ) &&
                json.Contains(
                    "\"SalesReceipt\"",
                    StringComparison.Ordinal
                );
        }

        private static bool EsFacturaCredito(
            string? json)
        {
            return
                !string.IsNullOrWhiteSpace(
                    json
                ) &&
                json.Contains(
                    "\"Invoice\"",
                    StringComparison.Ordinal
                );
        }

        private static string LimpiarNit(
            string? nit)
        {
            if (string.IsNullOrWhiteSpace(
                nit))
            {
                return "CF";
            }

            string cleaned =
                nit.Trim()
                    .Replace("-", "")
                    .Replace(" ", "");

            return string.IsNullOrWhiteSpace(
                cleaned)
                    ? "CF"
                    : cleaned;
        }

        private static string ObtenerTipoPrecio(
            string? requestedPriceType,
            string saleType,
            Invoice? storedInvoice)
        {
            if (storedInvoice != null &&
                storedInvoice.IsCertified &&
                !string.IsNullOrWhiteSpace(
                    storedInvoice.PriceType))
            {
                return storedInvoice.PriceType
                    .Trim()
                    .ToLowerInvariant();
            }

            string normalized =
                (requestedPriceType ?? "")
                    .Trim()
                    .ToLowerInvariant()
                    .Replace("é", "e")
                    .Replace("í", "i");

            if (string.IsNullOrWhiteSpace(
                normalized))
            {
                normalized =
                    saleType.Equals(
                        "credito",
                        StringComparison.OrdinalIgnoreCase)
                        ? "credito"
                        : "contado";
            }

            if (normalized != "contado" &&
                normalized != "credito")
            {
                throw new Exception(
                    "El tipo de precio debe ser contado o crédito."
                );
            }

            return normalized;
        }

        private static decimal ObtenerPorcentajeCredito(
            string priceType,
            decimal requestedPercentage,
            Invoice? storedInvoice)
        {
            if (storedInvoice != null &&
                storedInvoice.IsCertified)
            {
                return storedInvoice
                    .CreditPercentage;
            }

            if (priceType == "contado")
            {
                return 0m;
            }

            if (requestedPercentage <= 0m)
            {
                return 3m;
            }

            if (requestedPercentage != 3m)
            {
                throw new Exception(
                    "El porcentaje para precio crédito debe ser 3%."
                );
            }

            return requestedPercentage;
        }

        private static string?
            ValidarDatosFiscalesParaCertificar(
                string nit,
                string? customerName)
        {
            if (nit.Equals(
                "CF",
                StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(
                customerName))
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
            var lines =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (
                ItemDiscountRequest discount
                in discounts)
            {
                if (discount == null)
                {
                    continue;
                }

                string lineId =
                    discount.LineId?.Trim()
                    ?? "";

                if (string.IsNullOrWhiteSpace(
                    lineId))
                {
                    return
                        "Todos los descuentos deben indicar el LineId.";
                }

                if (discount.Amount < 0m)
                {
                    return
                        $"El descuento de la línea {lineId} no puede ser negativo.";
                }

                if (!lines.Add(
                    lineId))
                {
                    return
                        $"La línea {lineId} está repetida en la lista de descuentos.";
                }
            }

            return null;
        }
    }
}