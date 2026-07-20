using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.DTOs;
using QBTicketsApi.Services;
using System.Security.Claims;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/cash-movements")]
    public class CashMovementsController : ControllerBase
    {
        private readonly CashMovementService _cashMovementService;

        public CashMovementsController(
            CashMovementService cashMovementService)
        {
            _cashMovementService = cashMovementService;
        }

        // POST /api/cash-movements/opening-balance
        [HttpPost("opening-balance")]
        public async Task<IActionResult> SaveOpeningBalance(
            [FromBody] SaveOpeningBalanceRequestDto request)
        {
            try
            {
                string cashierName =
                    ResolveCashierName(request.CashierName);

                request.CashierName = cashierName;

                CashMovementResponseDto result =
                    await _cashMovementService
                        .SaveOpeningBalanceAsync(
                            request,
                            GetCurrentUserName()
                        );

                return Ok(new
                {
                    success = true,
                    message =
                        "Valor inicial guardado correctamente.",
                    movement = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        success = false,
                        error = ex.Message
                    }
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

        // POST /api/cash-movements/expense
        [HttpPost("expense")]
        public async Task<IActionResult> SaveExpense(
            [FromBody] SaveExpenseRequestDto request)
        {
            try
            {
                string cashierName =
                    ResolveCashierName(request.CashierName);

                request.CashierName = cashierName;

                CashMovementResponseDto result =
                    await _cashMovementService
                        .SaveExpenseAsync(
                            request,
                            GetCurrentUserName()
                        );

                return Ok(new
                {
                    success = true,
                    message =
                        "Gasto guardado correctamente.",
                    movement = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        success = false,
                        error = ex.Message
                    }
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

        // GET /api/cash-movements?cashierName=FERNANDO GOMEZ&date=2026-07-20
        [HttpGet]
        public async Task<IActionResult> GetMovements(
            [FromQuery] string? cashierName,
            [FromQuery] DateTime date)
        {
            try
            {
                string finalCashierName =
                    ResolveCashierName(cashierName);

                List<CashMovementResponseDto> movements =
                    await _cashMovementService
                        .GetMovementsAsync(
                            finalCashierName,
                            date
                        );

                return Ok(new
                {
                    success = true,
                    cashierName = finalCashierName,
                    date = date.Date,
                    movements
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        success = false,
                        error = ex.Message
                    }
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

        private string ResolveCashierName(
            string? requestedCashier)
        {
            string currentCashier =
                GetCurrentCashierName();

            /*
             * Fernando o cualquier usuario con permiso
             * general puede consultar cualquier cajero.
             */
            if (CanViewAllSales())
            {
                if (string.IsNullOrWhiteSpace(
                    requestedCashier))
                {
                    throw new Exception(
                        "Debe seleccionar un cajero."
                    );
                }

                return requestedCashier.Trim();
            }

            /*
             * Un cajero común solo puede registrar
             * movimientos para su propia caja.
             */
            if (string.IsNullOrWhiteSpace(
                currentCashier))
            {
                throw new UnauthorizedAccessException(
                    "El usuario no tiene un cajero asignado."
                );
            }

            if (!string.IsNullOrWhiteSpace(
                    requestedCashier) &&
                !string.Equals(
                    requestedCashier.Trim(),
                    currentCashier,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "No puede registrar movimientos para otro cajero."
                );
            }

            return currentCashier;
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

        private string GetCurrentUserName()
        {
            return User.FindFirst(
                       ClaimTypes.Name
                   )?.Value
                   ?? User.FindFirst(
                       "name"
                   )?.Value
                   ?? User.FindFirst(
                       "username"
                   )?.Value
                   ?? "Usuario";
        }
    }
}