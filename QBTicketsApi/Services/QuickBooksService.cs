using QBTicketsApi.Database;
using QBTicketsApi.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace QBTicketsApi.Services
{
    public class QuickBooksService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;

        public QuickBooksService(AppDbContext db, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<string> GetSalesReceipts()
        {
            var connection = _db.QuickBooksConnections.FirstOrDefault();

            if (connection == null)
                return "No hay conexión QuickBooks.";

            var client = _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", connection.AccessToken);

            string realmId = connection.RealmId;

            string query = Uri.EscapeDataString(
                "SELECT * FROM SalesReceipt MAXRESULTS 10"
            );

            string url =
                $"https://quickbooks.api.intuit.com/v3/company/{realmId}/query?query={query}";

            var response = await client.GetAsync(url);

            return await response.Content.ReadAsStringAsync();
        }
    }
}