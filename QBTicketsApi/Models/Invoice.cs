namespace QBTicketsApi.Models
{
    public class Invoice
    {
        public int Id { get; set; }

        public string QuickBooksId { get; set; } = "";

        public string InvoiceNumber { get; set; } = "";

        public string CustomerName { get; set; } = "";

        public string CustomerNit { get; set; } = "CF";

        public DateTime IssueDate { get; set; }

        /*
         * Subtotal antes del descuento.
         */
        public decimal Subtotal { get; set; }

        /*
         * Descuento total aplicado.
         */
        public decimal DiscountTotal { get; set; }

        /*
         * Total final después del descuento.
         */
        public decimal Total { get; set; }

        public string SaleType { get; set; } = "contado";

        /*
         * contado
         * credito
         */
        public string PriceType { get; set; } = "contado";

        /*
         * Para crédito será 3.
         * Para contado será 0.
         */
        public decimal CreditPercentage { get; set; }

        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } =
            DateTime.UtcNow;

        public string FelSerie { get; set; } = "";

        public string FelDteNumber { get; set; } = "";

        public string FelAuthorizationNumber { get; set; } = "";

        public DateTime? FelCertificationDate { get; set; }

        public string FelQr { get; set; } = "";

        public string FelCertifierName { get; set; } = "";

        public string FelCertifierNit { get; set; } = "";

        public bool IsCertified { get; set; }

        public bool IsCancelled { get; set; }

        public string CancellationReason { get; set; } = "";

        public DateTime? CancellationDate { get; set; }

        public string FelCancellationAuthorizationNumber
        {
            get;
            set;
        } = "";

        public string FelCancellationXml { get; set; } = "";

        public List<InvoiceLine> Lines { get; set; } =
            new List<InvoiceLine>();
    }
}