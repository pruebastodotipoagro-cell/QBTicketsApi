namespace QBTicketsApi.DTOs
{
    public class ItemDiscountRequest
    {
        public string LineId { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public class DiscountedTicketRequest
    {
        public string? Nit { get; set; }
        public string? CustomerName { get; set; }

        public List<ItemDiscountRequest> Discounts { get; set; } =
            new List<ItemDiscountRequest>();
    }
}