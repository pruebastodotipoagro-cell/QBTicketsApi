namespace QBTicketsApi.DTOs
{
    public class InvoiceItemDto
    {
        public string LineId { get; set; } = "";
        public string ItemId { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
        public decimal CurrentDiscount { get; set; }
        public decimal Total { get; set; }
    }

    public class InvoiceItemsResponseDto
    {
        public string QuickBooksId { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string SaleType { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal Total { get; set; }

        public List<InvoiceItemDto> Items { get; set; } =
            new List<InvoiceItemDto>();
    }
}