namespace QBTicketsApi.DTOs
{
    public class SaveOpeningBalanceRequestDto
    {
        public string CashierName { get; set; } = "";

        public DateTime Date { get; set; }

        public decimal Amount { get; set; }
    }

    public class SaveExpenseRequestDto
    {
        public string CashierName { get; set; } = "";

        public DateTime Date { get; set; }

        public decimal Amount { get; set; }

        public string Description { get; set; } = "";
    }

    public class CashMovementResponseDto
    {
        public int Id { get; set; }

        public string CashierName { get; set; } = "";

        public DateTime Date { get; set; }

        public string MovementType { get; set; } = "";

        public decimal Amount { get; set; }

        public string Description { get; set; } = "";

        public string CreatedBy { get; set; } = "";
    }
}