namespace QBTicketsApi.DTOs
{
    public class CreditCustomerSummaryDto
    {
        public string CustomerName { get; set; } = "";
        public string CustomerNit { get; set; } = "CF";
        public decimal TotalDebt { get; set; }
        public int OpenInvoices { get; set; }
        public string LastInvoiceId { get; set; } = "";
        public string LastInvoiceNumber { get; set; } = "";
    }
}