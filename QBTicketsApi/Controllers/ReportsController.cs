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

        public ReportsController(
            ReportsService reportsService)
        {
            _reportsService =
                reportsService;
        }

        // Todos los usuarios autenticados pueden ver
        // el reporte completo de ventas.
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

        // Todos los usuarios autenticados pueden abrir
        // el detalle desde Reportes.
        [HttpGet("sales/{id}/detail")]
        public async Task<IActionResult> GetSaleDetail(
            string id)
        {
            try
            {
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

        // Todos los usuarios autenticados pueden reintentar
        // una certificación pendiente desde Reportes.
        [HttpPost("sales/{id}/retry-certification")]
        public async Task<IActionResult> RetryCertification(
            string id,
            [FromBody] RetryCertificationRequestDto request)
        {
            try
            {
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

        // El cajero utilizado es exactamente el seleccionado
        // en el Native. Ya no se reemplaza por el usuario conectado.
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

        // Todos los usuarios autenticados pueden consultar
        // el corte general.
        [HttpGet("general-cut")]
        public async Task<IActionResult> GetGeneralCut(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
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

        // Todos los usuarios autenticados pueden consultar
        // el reporte completo por producto.
        [HttpGet("products")]
        public async Task<IActionResult> GetProductsReport(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
                ProductSalesReportResponseDto result =
                    await _reportsService
                        .GetProductSalesReportAsync(
                            desde,
                            hasta,
                            null
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
    }
}