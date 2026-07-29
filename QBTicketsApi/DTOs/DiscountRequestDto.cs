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

        public bool CertifyFel { get; set; } = true;

        /*
         * contado
         * credito
         */
        public string PriceType { get; set; } = "contado";

        /*
         * 0 para contado.
         * 3 para precio crédito.
         */
        public decimal CreditPercentage { get; set; }

        public List<ItemDiscountRequest> Discounts { get; set; } =
            new List<ItemDiscountRequest>();
    }
    public class DashboardSyncRequest
    {
        public string PriceType { get; set; } = "contado";
        public decimal CreditPercentage { get; set; }
        public List<ItemDiscountRequest> Discounts { get; set; } = new List<ItemDiscountRequest>();
    }

    public class DashboardSyncResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string QuickBooksId { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal Total { get; set; }
        public string PriceType { get; set; } = "contado";
        public bool WasAlreadySynchronized { get; set; }
    }

}