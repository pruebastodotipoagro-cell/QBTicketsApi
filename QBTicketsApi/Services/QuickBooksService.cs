using QBTicketsApi.Database;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QBTicketsApi.DTOs;
using System.Text.Json;

namespace QBTicketsApi.Services
{
    public class QuickBooksService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public QuickBooksService(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        public async Task<string> GetSalesReceipts()
        {
            var connection = _db.QuickBooksConnections.FirstOrDefault();
            if (connection == null) return "No hay conexión QuickBooks.";

            if (connection.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
                await RefreshToken();

            connection = _db.QuickBooksConnections.FirstOrDefault();

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", connection.AccessToken);

            string query = Uri.EscapeDataString("SELECT * FROM SalesReceipt MAXRESULTS 10");
            string url = $"https://quickbooks.api.intuit.com/v3/company/{connection.RealmId}/query?query={query}";

            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        private async Task RefreshToken()
        {
            var connection = _db.QuickBooksConnections.FirstOrDefault();
            if (connection == null) throw new Exception("No hay conexión QuickBooks.");

            string clientId = (_config["QuickBooks:ClientId"] ?? "").Trim();
            string clientSecret = (_config["QuickBooks:ClientSecret"] ?? "").Trim();

            var client = _httpClientFactory.CreateClient();

            string basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var form = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", connection.RefreshToken }
            };

            var response = await client.PostAsync(
                "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer",
                new FormUrlEncodedContent(form)
            );

            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception("Error refrescando token QuickBooks: " + json);

            var token = JsonSerializer.Deserialize<QuickBooksTokenResponse>(json);

            connection.AccessToken = token.AccessToken;
            connection.RefreshToken = token.RefreshToken;
            connection.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
            connection.RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(token.RefreshTokenExpiresIn);
            connection.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task<string> GetSalesReceiptById(string id)
        {
            var connection = _db.QuickBooksConnections.FirstOrDefault();
            if (connection == null) return "";

            if (connection.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
                await RefreshToken();

            connection = _db.QuickBooksConnections.FirstOrDefault();

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", connection.AccessToken);

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            string query = Uri.EscapeDataString($"SELECT * FROM SalesReceipt WHERE Id = '{id}'");
            string url = $"https://quickbooks.api.intuit.com/v3/company/{connection.RealmId}/query?query={query}";

            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetCreditInvoices()
        {
            var connection = _db.QuickBooksConnections.FirstOrDefault();
            if (connection == null) return "No hay conexión QuickBooks.";

            if (connection.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
                await RefreshToken();

            connection = _db.QuickBooksConnections.FirstOrDefault();

            var client = _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", connection.AccessToken);

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            string query = Uri.EscapeDataString("SELECT * FROM Invoice MAXRESULTS 20");
            string url = $"https://quickbooks.api.intuit.com/v3/company/{connection.RealmId}/query?query={query}";

            var response = await client.GetAsync(url);

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<InvoiceResponseDto>> GetCreditInvoicesList()
        {
            var json = await GetCreditInvoices();

            var result = new List<InvoiceResponseDto>();

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("QueryResponse", out var queryResponse))
                return result;

            if (!queryResponse.TryGetProperty("Invoice", out var invoices))
                return result;

            foreach (var inv in invoices.EnumerateArray())
            {
                string id = inv.TryGetProperty("Id", out var idValue) ? idValue.GetString() ?? "" : "";
                string docNumber = inv.TryGetProperty("DocNumber", out var docValue) ? docValue.GetString() ?? "" : id;

                string customerName = "Consumidor Final";
                if (inv.TryGetProperty("CustomerRef", out var customerRef))
                    customerName = customerRef.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? customerName : customerName;

                DateTime issueDate = DateTime.UtcNow;
                if (inv.TryGetProperty("TxnDate", out var dateValue))
                    DateTime.TryParse(dateValue.GetString(), out issueDate);

                decimal total = 0;
                if (inv.TryGetProperty("TotalAmt", out var totalValue))
                    totalValue.TryGetDecimal(out total);

                decimal balance = 0;
                if (inv.TryGetProperty("Balance", out var balanceValue))
                    balanceValue.TryGetDecimal(out balance);

                result.Add(new InvoiceResponseDto
                {
                    QbInvoiceId = id,
                    InvoiceNumber = docNumber,
                    CustomerName = customerName,
                    CustomerNit = "CF",
                    IssueDate = issueDate,
                    Total = total,
                    Balance = balance,
                    SaleType = "credito"
                });
            }

            return result;
        }

        private class QuickBooksTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("x_refresh_token_expires_in")]
            public int RefreshTokenExpiresIn { get; set; }
        }
    }
}