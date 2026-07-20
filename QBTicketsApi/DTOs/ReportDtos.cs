namespace QBTicketsApi.DTOs
{
    namespace QBTicketsApi.DTOs
    {
        public class SalesReportResponseDto
        {
            public List<SalesReportRowDto> Sales { get; set; }
                = new List<SalesReportRowDto>();

            public decimal Total { get; set; }
        }

        public class SalesReportRowDto
        {
            public string QuickBooksId { get; set; } = "";

            public DateTime IssueDate { get; set; }

            public string SaleType { get; set; } = "";

            public string PaymentMethod { get; set; } = "";

            public string CustomerName { get; set; } = "";

            public string CustomerNit { get; set; } = "CF";

            public string InvoiceNumber { get; set; } = "";

            public string Correlative { get; set; } = "";

            public string AuthorizationNumber { get; set; } = "";

            public string Serie { get; set; } = "";

            public string DteNumber { get; set; } = "";

            public string Status { get; set; } = "";

            public decimal Total { get; set; }

            public string CancellationReason { get; set; } = "";

            public string CashierName { get; set; } = "";

            public bool IsCertified { get; set; }

            public bool CanRetryCertification { get; set; }
        }

        public class SaleDetailDto
        {
            public string QuickBooksId { get; set; } = "";

            public string CustomerName { get; set; } = "";

            public string InvoiceName { get; set; } = "";

            public string CustomerNit { get; set; } = "CF";

            public string Correlative { get; set; } = "";

            public string DocumentNumber { get; set; } = "";

            public string DteType { get; set; } = "FACT";

            public string Voucher { get; set; } = "";

            public string PaymentMethod { get; set; } = "";

            public string Address { get; set; } = "";

            public DateTime IssueDate { get; set; }

            public string AuthorizationNumber { get; set; } = "";

            public string Serie { get; set; } = "";

            public string DteNumber { get; set; } = "";

            public string Status { get; set; } = "";

            public string Qr { get; set; } = "";

            public string CashierName { get; set; } = "";

            public string SaleType { get; set; } = "";

            public decimal Subtotal { get; set; }

            public decimal DiscountTotal { get; set; }

            public decimal Total { get; set; }

            public bool IsCertified { get; set; }

            public bool CanRetryCertification { get; set; }

            public List<SaleDetailItemDto> Items { get; set; }
                = new List<SaleDetailItemDto>();
        }

        public class SaleDetailItemDto
        {
            public string LineId { get; set; } = "";

            public string ItemId { get; set; } = "";

            public decimal Quantity { get; set; }

            public string Description { get; set; } = "";

            public decimal UnitPrice { get; set; }

            public decimal Discount { get; set; }

            public decimal Total { get; set; }
        }

        public class RetryCertificationRequestDto
        {
            public string Nit { get; set; } = "CF";

            public string CustomerName { get; set; }
                = "Consumidor Final";
        }

        public class RetryCertificationResponseDto
        {
            public bool Success { get; set; }

            public string Message { get; set; } = "";

            public string Serie { get; set; } = "";

            public string DteNumber { get; set; } = "";

            public string AuthorizationNumber { get; set; } = "";

            public DateTime CertificationDate { get; set; }

            public string Qr { get; set; } = "";
        }

        public class CashierCutDto
        {
            public DateTime Date { get; set; }

            public string CashierName { get; set; } = "";

            public decimal OpeningBalance { get; set; }

            public decimal TotalExpenses { get; set; }

            public decimal CashSales { get; set; }

            public decimal CheckSales { get; set; }

            public decimal CreditCardSales { get; set; }

            public decimal CreditPayments { get; set; }
        }

        public class GeneralCutDto
        {
            public DateTime StartDate { get; set; }

            public DateTime EndDate { get; set; }

            public decimal TotalExpenses { get; set; }

            public decimal CheckSales { get; set; }

            public decimal CashSales { get; set; }

            public decimal CreditCardSales { get; set; }

            public decimal CreditPayments { get; set; }

            public decimal TotalSales { get; set; }
        }

        public class CreditPaymentDto
        {
            public string PaymentId { get; set; } = "";

            public DateTime PaymentDate { get; set; }

            public string CustomerName { get; set; } = "";

            public string PaymentMethod { get; set; } = "";

            public string ReferenceNumber { get; set; } = "";

            public decimal TotalAmount { get; set; }

            public List<string> InvoiceIds { get; set; }
                = new List<string>();
        }

        public class ProductSalesReportResponseDto
        {
            public List<ProductSalesReportRowDto> Products { get; set; }
                = new List<ProductSalesReportRowDto>();
        }

        public class ProductSalesReportRowDto
        {
            public string ItemId { get; set; } = "";

            public string ProductName { get; set; } = "";

            public string Brand { get; set; } = "";

            public decimal QuantitySold { get; set; }

            public decimal AccumulatedCost { get; set; }

            public decimal AccumulatedSale { get; set; }

            public decimal Profit { get; set; }
        }

        public class QuickBooksItemReportDto
        {
            public string ItemId { get; set; } = "";

            public string Name { get; set; } = "";

            public string Brand { get; set; } = "";

            public decimal PurchaseCost { get; set; }

            public decimal UnitPrice { get; set; }
        }
    }
}