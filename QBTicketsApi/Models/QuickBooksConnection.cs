namespace QBTicketsApi.Models
{
    public class QuickBooksConnection
    {
        public int Id { get; set; }

        public string RealmId { get; set; }

        public string Environment { get; set; } = "production";

        public string AccessToken { get; set; }

        public string RefreshToken { get; set; }

        public DateTime AccessTokenExpiresAt { get; set; }

        public DateTime RefreshTokenExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}