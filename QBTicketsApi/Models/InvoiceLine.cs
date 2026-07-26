using System.ComponentModel.DataAnnotations;

namespace QBTicketsApi.Models
{
    public class InvoiceLine
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }

        public Invoice Invoice { get; set; } = null!;

        [MaxLength(100)]
        public string QuickBooksLineId { get; set; } = "";

        [MaxLength(100)]
        public string QuickBooksItemId { get; set; } = "";

        [MaxLength(500)]
        public string Description { get; set; } = "";

        public decimal Quantity { get; set; }

        /*
         * Precio original que tenía el producto
         * antes de aplicar precio crédito.
         */
        public decimal OriginalUnitPrice { get; set; }

        /*
         * Precio realmente utilizado en la factura.
         */
        public decimal AppliedUnitPrice { get; set; }

        public decimal OriginalSubtotal { get; set; }

        public decimal DiscountAmount { get; set; }

        public decimal FinalTotal { get; set; }

        public DateTime CreatedAt { get; set; } =
            DateTime.UtcNow;
    }
}