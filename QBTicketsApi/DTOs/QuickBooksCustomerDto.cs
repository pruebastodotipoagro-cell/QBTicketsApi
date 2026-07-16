namespace QBTicketsApi.DTOs
{
    public class QuickBooksCustomerDto
    {
        public string CustomerId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Nit { get; set; } = "CF";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public bool Active { get; set; }
    }
}