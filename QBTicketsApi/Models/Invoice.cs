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
    }
}