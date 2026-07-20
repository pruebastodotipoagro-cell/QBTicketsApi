using System.ComponentModel.DataAnnotations;

namespace QBTicketsApi.Models
{
    public class CashMovement
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string CashierName { get; set; } = "";

        public DateTime MovementDate { get; set; }

        /*
         * OPENING_BALANCE = valor inicial de caja
         * EXPENSE = gasto o salida de dinero
         */
        [Required]
        [MaxLength(30)]
        public string MovementType { get; set; } = "";

        public decimal Amount { get; set; }

        [MaxLength(300)]
        public string Description { get; set; } = "";

        /*
         * Usuario de la aplicación que registró
         * el movimiento.
         */
        [MaxLength(100)]
        public string CreatedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; }
            = DateTime.UtcNow;
    }
}