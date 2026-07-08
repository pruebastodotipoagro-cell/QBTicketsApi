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
        public decimal Total { get; set; }

        public string SaleType { get; set; } = "contado";
        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string FelSerie { get; set; } = "";
        public string FelDteNumber { get; set; } = "";
        public string FelAuthorizationNumber { get; set; } = "";
        public DateTime? FelCertificationDate { get; set; }
        public string FelQr { get; set; } = "";
        public string FelCertifierName { get; set; } = "";
        public string FelCertifierNit { get; set; } = "";
        public bool IsCertified { get; set; } = false;
    }
}