using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.DTOs;
using QBTicketsApi.Services;
using System.Globalization;
using System.Text;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/reports")]
    public class ReportsController : ControllerBase
    {
        private readonly ReportsService _reportsService;

        public ReportsController(
            ReportsService reportsService)
        {
            _reportsService = reportsService;
        }

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

                HashSet<string>? allowedCashiers =
                    GetAllowedCashiersForCurrentUser();

                if (allowedCashiers != null)
                {
                    result.Sales =
                        result.Sales
                            .Where(x =>
                                allowedCashiers.Contains(
                                    NormalizeName(
                                        x.CashierName
                                    )
                                )
                            )
                            .ToList();

                    result.Total =
                        result.Sales.Sum(
                            x => x.Total
                        );
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

        [HttpGet("sales/{id}/detail")]
        public async Task<IActionResult> GetSaleDetail(
            string id)
        {
            try
            {
                SaleDetailDto result =
                    await _reportsService
                        .GetSaleDetailAsync(id);

                if (!CurrentUserCanAccessCashier(
                        result.CashierName))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            error =
                                "No tiene permiso para consultar ventas de la otra sucursal."
                        }
                    );
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

        [HttpPost("sales/{id}/retry-certification")]
        public async Task<IActionResult> RetryCertification(
            string id,
            [FromBody] RetryCertificationRequestDto request)
        {
            try
            {
                SaleDetailDto detail =
                    await _reportsService
                        .GetSaleDetailAsync(id);

                if (!CurrentUserCanAccessCashier(
                        detail.CashierName))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            error =
                                "No tiene permiso para certificar ventas de la otra sucursal."
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

        [HttpGet("cashier-cut")]
        public async Task<IActionResult> GetCashierCut(
            [FromQuery] string? cashierName,
            [FromQuery] DateTime date,
            [FromQuery] decimal openingBalance = 0)
        {
            try
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

                string finalCashierName =
                    cashierName.Trim();

                if (!CurrentUserCanAccessCashier(
                        finalCashierName))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            error =
                                "No tiene permiso para consultar el corte de un cajero de la otra sucursal."
                        }
                    );
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

        [HttpGet("general-cut")]
        public async Task<IActionResult> GetGeneralCut(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
                if (GetAllowedCashiersForCurrentUser() != null)
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            error =
                                "El corte general completo está restringido por sucursal."
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

        [HttpGet("products")]
        public async Task<IActionResult> GetProductsReport(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
                string? cashierName =
                    GetSingleCashierForProductReport();

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

        private bool CurrentUserCanAccessCashier(
            string? documentCashier)
        {
            HashSet<string>? allowed =
                GetAllowedCashiersForCurrentUser();

            if (allowed == null)
            {
                return true;
            }

            return allowed.Contains(
                NormalizeName(
                    documentCashier
                )
            );
        }

        private HashSet<string>?
            GetAllowedCashiersForCurrentUser()
        {
            string currentCashier =
                NormalizeName(
                    User.FindFirst(
                        "cashierName"
                    )?.Value
                );

            if (currentCashier ==
                    "CARLOS LORENZANA" ||
                currentCashier ==
                    "PAOLA VALLADARES")
            {
                return new HashSet<string>(
                    new[]
                    {
                        "CARLOS LORENZANA",
                        "PAOLA VALLADARES"
                    },
                    StringComparer.OrdinalIgnoreCase
                );
            }

            if (currentCashier ==
                    "ROCIO RAMOS" ||
                currentCashier ==
                    "ADAN HERNANDEZ" ||
                currentCashier ==
                    "FERNANDO GOMEZ")
            {
                return new HashSet<string>(
                    new[]
                    {
                        "ROCIO RAMOS",
                        "ADAN HERNANDEZ",
                        "FERNANDO GOMEZ"
                    },
                    StringComparer.OrdinalIgnoreCase
                );
            }

            return null;
        }

        private string? GetSingleCashierForProductReport()
        {
            string currentCashier =
                NormalizeName(
                    User.FindFirst(
                        "cashierName"
                    )?.Value
                );

            if (currentCashier ==
                    "CARLOS LORENZANA" ||
                currentCashier ==
                    "PAOLA VALLADARES" ||
                currentCashier ==
                    "ROCIO RAMOS" ||
                currentCashier ==
                    "ADAN HERNANDEZ" ||
                currentCashier ==
                    "FERNANDO GOMEZ")
            {
                return currentCashier;
            }

            return null;
        }

        private static string NormalizeName(
            string? value)
        {
            string normalized =
                (value ?? "")
                    .Trim()
                    .ToUpperInvariant()
                    .Normalize(
                        NormalizationForm.FormD
                    );

            var builder =
                new StringBuilder();

            foreach (char character in normalized)
            {
                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(
                        character
                    );

                if (category !=
                    UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }

            string clean =
                builder
                    .ToString()
                    .Normalize(
                        NormalizationForm.FormC
                    )
                    .Replace(".", " ")
                    .Replace(",", " ")
                    .Replace("-", " ")
                    .Replace("  ", " ")
                    .Trim();

            if (clean == "ROCIO")
                return "ROCIO RAMOS";

            if (clean == "ADAN")
                return "ADAN HERNANDEZ";

            if (clean == "FERNANDO")
                return "FERNANDO GOMEZ";

            if (clean == "CARLOS")
                return "CARLOS LORENZANA";

            if (clean == "PAOLA")
                return "PAOLA VALLADARES";

            return clean;
        }
    }
}