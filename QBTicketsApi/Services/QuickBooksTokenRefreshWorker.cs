using QBTicketsApi.Database;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBTicketsApi.Services
{
    public class QuickBooksTokenRefreshWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public QuickBooksTokenRefreshWorker(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshExpiringTokens(stoppingToken);
                }
                catch
                {
                    // Luego agregamos logs reales
                }

                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        private async Task RefreshExpiringTokens(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var connections = await db.QuickBooksConnections.ToListAsync(stoppingToken);

            foreach (var connection in connections)
            {
                if (connection.AccessTokenExpiresAt > DateTime.UtcNow.AddMinutes(10))
                    continue;

                string clientId = (_config["QuickBooks:ClientId"] ?? "").Trim();
                string clientSecret = (_config["QuickBooks:ClientSecret"] ?? "").Trim();

                var client = _httpClientFactory.CreateClient();

                string basicAuth = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")
                );

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", basicAuth);

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json")
                );

                var form = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", connection.RefreshToken }
                };

                var response = await client.PostAsync(
                    "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer",
                    new FormUrlEncodedContent(form),
                    stoppingToken
                );

                string json = await response.Content.ReadAsStringAsync(stoppingToken);

                if (!response.IsSuccessStatusCode)
                    continue;

                var token = JsonSerializer.Deserialize<QuickBooksTokenResponse>(json);

                if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
                    continue;

                connection.AccessToken = token.AccessToken;
                connection.RefreshToken = token.RefreshToken;
                connection.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
                connection.RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(token.RefreshTokenExpiresIn);
                connection.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(stoppingToken);
        }

        private class QuickBooksTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = "";

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = "";

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("x_refresh_token_expires_in")]
            public int RefreshTokenExpiresIn { get; set; }
        }
    }
}