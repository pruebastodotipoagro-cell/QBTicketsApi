namespace QBTicketsApi.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = "";

        public string Password { get; set; } = "";

        public string Name { get; set; } = "";

        public string Role { get; set; } = "";

        // Nombre exacto asociado al campo CAJERO de QuickBooks.
        // Ejemplo: ADAN HERNANDEZ
        public string CashierName { get; set; } = "";

        // true: puede ver todas las ventas.
        // false: solo puede ver sus propias ventas.
        public bool CanViewAllSales { get; set; }
    }
}