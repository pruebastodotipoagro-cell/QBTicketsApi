using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.Models;

namespace QBTicketsApi.Services
{
    public class CashMovementService
    {
        private readonly AppDbContext _db;

        public CashMovementService(
            AppDbContext db)
        {
            _db = db;
        }

        public async Task<CashMovementResponseDto>
            SaveOpeningBalanceAsync(
                SaveOpeningBalanceRequestDto request,
                string createdBy)
        {
            ValidateCashier(
                request.CashierName
            );

            if (request.Amount < 0)
            {
                throw new Exception(
                    "El valor inicial no puede ser negativo."
                );
            }

            DateTime movementDate =
                request.Date.Date;

            /*
             * Solo debe existir un valor inicial por
             * cajero y por fecha.
             */
            CashMovement? existing =
                await _db.CashMovements
                    .FirstOrDefaultAsync(x =>
                        x.CashierName ==
                            request.CashierName.Trim() &&
                        x.MovementType ==
                            "OPENING_BALANCE" &&
                        x.MovementDate.Date ==
                            movementDate
                    );

            if (existing == null)
            {
                existing = new CashMovement
                {
                    CashierName =
                        request.CashierName.Trim(),

                    MovementDate =
                        movementDate,

                    MovementType =
                        "OPENING_BALANCE",

                    Amount =
                        request.Amount,

                    Description =
                        "Valor inicial de caja",

                    CreatedBy =
                        createdBy,

                    CreatedAt =
                        DateTime.UtcNow
                };

                _db.CashMovements.Add(existing);
            }
            else
            {
                /*
                 * Si ya se había registrado, se actualiza.
                 */
                existing.Amount =
                    request.Amount;

                existing.CreatedBy =
                    createdBy;

                existing.CreatedAt =
                    DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            return Map(existing);
        }

        public async Task<CashMovementResponseDto>
            SaveExpenseAsync(
                SaveExpenseRequestDto request,
                string createdBy)
        {
            ValidateCashier(
                request.CashierName
            );

            if (request.Amount <= 0)
            {
                throw new Exception(
                    "El gasto debe ser mayor que cero."
                );
            }

            if (string.IsNullOrWhiteSpace(
                request.Description))
            {
                throw new Exception(
                    "Debe indicar la descripción del gasto."
                );
            }

            var movement =
                new CashMovement
                {
                    CashierName =
                        request.CashierName.Trim(),

                    MovementDate =
                        request.Date.Date,

                    MovementType =
                        "EXPENSE",

                    Amount =
                        request.Amount,

                    Description =
                        request.Description.Trim(),

                    CreatedBy =
                        createdBy,

                    CreatedAt =
                        DateTime.UtcNow
                };

            _db.CashMovements.Add(movement);

            await _db.SaveChangesAsync();

            return Map(movement);
        }

        public async Task<decimal>
            GetOpeningBalanceAsync(
                string cashierName,
                DateTime date)
        {
            return await _db.CashMovements
                .AsNoTracking()
                .Where(x =>
                    x.CashierName == cashierName &&
                    x.MovementType ==
                        "OPENING_BALANCE" &&
                    x.MovementDate.Date ==
                        date.Date
                )
                .Select(x => x.Amount)
                .FirstOrDefaultAsync();
        }

        public async Task<decimal>
            GetExpensesAsync(
                string cashierName,
                DateTime date)
        {
            return await _db.CashMovements
                .AsNoTracking()
                .Where(x =>
                    x.CashierName == cashierName &&
                    x.MovementType ==
                        "EXPENSE" &&
                    x.MovementDate.Date ==
                        date.Date
                )
                .SumAsync(x => x.Amount);
        }

        public async Task<List<CashMovementResponseDto>>
            GetMovementsAsync(
                string cashierName,
                DateTime date)
        {
            List<CashMovement> movements =
                await _db.CashMovements
                    .AsNoTracking()
                    .Where(x =>
                        x.CashierName == cashierName &&
                        x.MovementDate.Date == date.Date
                    )
                    .OrderBy(x => x.CreatedAt)
                    .ToListAsync();

            return movements
                .Select(Map)
                .ToList();
        }

        private static void ValidateCashier(
            string cashierName)
        {
            if (string.IsNullOrWhiteSpace(
                cashierName))
            {
                throw new Exception(
                    "Debe indicar el cajero."
                );
            }
        }

        private static CashMovementResponseDto Map(
            CashMovement movement)
        {
            return new CashMovementResponseDto
            {
                Id =
                    movement.Id,

                CashierName =
                    movement.CashierName,

                Date =
                    movement.MovementDate,

                MovementType =
                    movement.MovementType,

                Amount =
                    movement.Amount,

                Description =
                    movement.Description,

                CreatedBy =
                    movement.CreatedBy
            };
        }
    }
}