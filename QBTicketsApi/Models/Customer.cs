namespace QBTicketsApi.Models
{
    public class Customer
    {
        public int Id { get; set; }

        public string QuickBooksCustomerId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Nit { get; set; } = "CF";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}