using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.DTOs;
using QBTicketsApi.DTOs.QBTicketsApi.DTOs;
using QBTicketsApi.Services;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/reports")]
    public class ReportsController : ControllerBase
    {
        private readonly ReportsService _reportsService;
        private readonly QuickBooksService _quickBooksService;

        public ReportsController(
            ReportsService reportsService,
            QuickBooksService quickBooksService)
        {
            _reportsService = reportsService;
            _quickBooksService = quickBooksService;
        }

        // GET /api/reports/sales?desde=2026-07-01&hasta=2026-07-20
        [HttpGet("sales")]
        public async Task<IActionResult> GetSales(
            [FromQuery] string? desde = null,
            [FromQuery] string? hasta = null)
        {
            try
            {
                SalesReportResponseDto result =
                    await _reportsService
                        .GetSalesReportAsync(
                            desde,
                            hasta
                        );

                if (!CanViewAllSales())
                {
                    string cashierName =
                        GetCurrentCashierName();

                    result.Sales =
                        result.Sales
                            .Where(x =>
                                string.Equals(
                                    x.CashierName?.Trim(),
                                    cashierName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            .ToList();

                    result.Total =
                        result.Sales.Sum(x => x.Total);
                }

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

        // GET /api/reports/sales/11724/detail
        [HttpGet("sales/{id}/detail")]
        public async Task<IActionResult> GetSaleDetail(
            string id)
        {
            try
            {
                if (!await UserCanAccessDocumentAsync(id))
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

                SaleDetailDto result =
                    await _reportsService
                        .GetSaleDetailAsync(id);

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

        // POST /api/reports/sales/11724/retry-certification
        [HttpPost("sales/{id}/retry-certification")]
        public async Task<IActionResult> RetryCertification(
            string id,
            [FromBody] RetryCertificationRequestDto request)
        {
            try
            {
                if (!await UserCanAccessDocumentAsync(id))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            error =
                                "No tiene permiso para certificar esta venta."
                        }
                    );
                }

                RetryCertificationResponseDto result =
                    await _reportsService
                        .RetryCertificationAsync(
                            id,
                            request
                        );

                if (!result.Success)
                {
                    return BadRequest(result);
                }

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

        // GET /api/reports/cashier-cut
        [HttpGet("cashier-cut")]
        public async Task<IActionResult> GetCashierCut(
            [FromQuery] string? cashierName,
            [FromQuery] DateTime date,
            [FromQuery] decimal openingBalance = 0)
        {
            try
            {
                string finalCashierName;

                if (CanViewAllSales())
                {
                    if (string.IsNullOrWhiteSpace(
                        cashierName))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error =
                                "Debe seleccionar un cajero."
                        });
                    }

                    finalCashierName =
                        cashierName.Trim();
                }
                else
                {
                    finalCashierName =
                        GetCurrentCashierName();

                    if (string.IsNullOrWhiteSpace(
                        finalCashierName))
                    {
                        return StatusCode(
                            StatusCodes.Status403Forbidden,
                            new
                            {
                                success = false,
                                error =
                                    "El usuario no tiene un cajero asignado."
                            }
                        );
                    }
                }

                CashierCutDto result =
                    await _reportsService
                        .GetCashierCutAsync(
                            finalCashierName,
                            date,
                            openingBalance
                        );

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

        // GET /api/reports/general-cut
        [HttpGet("general-cut")]
        public async Task<IActionResult> GetGeneralCut(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
                if (!CanViewAllSales())
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            error =
                                "No tiene permiso para consultar el corte general."
                        }
                    );
                }

                GeneralCutDto result =
                    await _reportsService
                        .GetGeneralCutAsync(
                            desde,
                            hasta
                        );

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
        // GET /api/reports/products?desde=2026-07-01&hasta=2026-07-20
        [HttpGet("products")]
        public async Task<IActionResult> GetProductsReport(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
                string? cashierName = null;

                if (!CanViewAllSales())
                {
                    cashierName =
                        GetCurrentCashierName();

                    if (string.IsNullOrWhiteSpace(
                        cashierName))
                    {
                        return StatusCode(
                            StatusCodes.Status403Forbidden,
                            new
                            {
                                success = false,
                                error =
                                    "El usuario no tiene un cajero asignado."
                            }
                        );
                    }
                }

                ProductSalesReportResponseDto result =
                    await _reportsService
                        .GetProductSalesReportAsync(
                            desde,
                            hasta,
                            cashierName
                        );

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

        private async Task<bool>
            UserCanAccessDocumentAsync(string id)
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
                       out bool canViewAll) &&
                   canViewAll;
        }
    }
}