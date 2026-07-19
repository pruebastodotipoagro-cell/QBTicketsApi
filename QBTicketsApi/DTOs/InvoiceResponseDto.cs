namespace QBTicketsApi.DTOs
{
    public class InvoiceResponseDto
    {
        public string QbInvoiceId { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string CustomerNit { get; set; } = "CF";
        public DateTime IssueDate { get; set; }
        public decimal Total { get; set; }
        public decimal Balance { get; set; }
        public string SaleType { get; set; } = "credito";

        public string CashierName { get; set; } = "";
    }
}