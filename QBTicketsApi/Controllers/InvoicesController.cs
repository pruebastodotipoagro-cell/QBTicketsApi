using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.DTOs;
using QBTicketsApi.Services;
using System.Security.Claims;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/invoices")]
    public class InvoicesController : ControllerBase
    {
        private readonly QuickBooksService _quickBooksService;

        public InvoicesController(
            QuickBooksService quickBooksService)
        {
            _quickBooksService = quickBooksService;
        }

        [HttpGet("quickbooks-test")]
        public async Task<IActionResult> QuickBooksTest()
        {
            if (!CanViewAllSales())
            {
                return Forbid();
            }

            string rawResult =
                await _quickBooksService
                    .GetSalesReceipts();

            return Content(
                rawResult,
                "application/json"
            );
        }

        [HttpGet("sales-receipts")]
        public async Task<IActionResult> GetSalesReceipts(
            [FromQuery] string? desde = null,
            [FromQuery] string? hasta = null)
        {
            try
            {
                List<InvoiceResponseDto> invoices =
                    await _quickBooksService
                        .GetSalesReceiptsList(
                            desde,
                            hasta
                        );

                invoices =
                    FilterInvoicesForCurrentUser(
                        invoices
                    );

                return Ok(new
                {
                    invoices
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

        [HttpGet("credit-invoices")]
        public async Task<IActionResult> GetCreditInvoices(
            [FromQuery] string? desde = null,
            [FromQuery] string? hasta = null)
        {
            try
            {
                List<InvoiceResponseDto> invoices =
                    await _quickBooksService
                        .GetCreditInvoicesList(
                            desde,
                            hasta
                        );

                invoices =
                    FilterInvoicesForCurrentUser(
                        invoices
                    );

                return Ok(new
                {
                    invoices
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

        [HttpGet("credit-summary")]
        public async Task<IActionResult> GetCreditSummary()
        {
            try
            {
                List<InvoiceResponseDto> invoices =
                    await _quickBooksService
                        .GetCreditInvoicesList();

                invoices =
                    FilterInvoicesForCurrentUser(
                        invoices
                    );

                var customers =
                    invoices
                        .Where(x => x.Balance > 0)
                        .GroupBy(x => x.CustomerName)
                        .Select(group =>
                        {
                            InvoiceResponseDto lastInvoice =
                                group
                                    .OrderByDescending(
                                        x => x.IssueDate
                                    )
                                    .First();

                            return new CreditCustomerSummaryDto
                            {
                                CustomerName =
                                    group.Key,

                                CustomerNit =
                                    lastInvoice.CustomerNit,

                                TotalDebt =
                                    group.Sum(
                                        x => x.Balance
                                    ),

                                OpenInvoices =
                                    group.Count(),

                                LastInvoiceId =
                                    lastInvoice.QbInvoiceId,

                                LastInvoiceNumber =
                                    lastInvoice.InvoiceNumber
                            };
                        })
                        .OrderBy(
                            x => x.CustomerName
                        )
                        .ToList();

                return Ok(new
                {
                    customers
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

        [HttpGet("{id}/items")]
        public async Task<IActionResult> GetDocumentItems(
            string id)
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

                bool hasAccess =
                    await UserCanAccessDocumentAsync(id);

                if (!hasAccess)
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            error =
                                "No tiene permiso para consultar esta venta."
                        }
                    );
                }

                InvoiceItemsResponseDto result =
                    await _quickBooksService
                        .GetDocumentItemsAsync(id);

                return Ok(result);
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

        [HttpGet("raw-sales-receipt/{id}")]
        public async Task<IActionResult> GetRawSalesReceipt(
            string id)
        {
            if (!CanViewAllSales())
            {
                return Forbid();
            }

            try
            {
                string json =
                    await _quickBooksService
                        .GetSalesReceiptById(id);

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

        private List<InvoiceResponseDto>
            FilterInvoicesForCurrentUser(
                List<InvoiceResponseDto> invoices)
        {
            if (CanViewAllSales())
            {
                return invoices;
            }

            string cashierName =
                GetCurrentCashierName();

            if (string.IsNullOrWhiteSpace(
                cashierName))
            {
                return new List<InvoiceResponseDto>();
            }

            return invoices
                .Where(x =>
                    string.Equals(
                        x.CashierName?.Trim(),
                        cashierName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();
        }

        private async Task<bool>
            UserCanAccessDocumentAsync(
                string id)
        {
            if (CanViewAllSales())
            {
                return true;
            }

            string currentCashier =
                GetCurrentCashierName();

            if (string.IsNullOrWhiteSpace(
                currentCashier))
            {
                return false;
            }

            string documentCashier =
                await _quickBooksService
                    .GetDocumentCashierNameAsync(id);

            return string.Equals(
                documentCashier?.Trim(),
                currentCashier,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private string GetCurrentCashierName()
        {
            return User.FindFirst(
                       "cashierName"
                   )?.Value?.Trim()
                   ?? "";
        }

        private bool CanViewAllSales()
        {
            string value =
                User.FindFirst(
                    "canViewAllSales"
                )?.Value
                ?? "false";

            return bool.TryParse(
                       value,
                       out bool canViewAll
                   ) &&
                   canViewAll;
        }
    }
}