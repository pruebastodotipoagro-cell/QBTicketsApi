using Microsoft.AspNetCore.Mvc;
using QBTicketsApi.Database;
using QBTicketsApi.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("auth")]
    public class QuickBooksAuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public QuickBooksAuthController(
            AppDbContext db,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        [HttpGet("quickbooks")]
        public IActionResult ConnectQuickBooks()
        {
            string clientId = _config["QuickBooks:ClientId"];
            string redirectUri = _config["QuickBooks:RedirectUri"];

            string scope = "com.intuit.quickbooks.accounting";
            string state = Guid.NewGuid().ToString("N");

            string url =
                "https://appcenter.intuit.com/connect/oauth2" +
                $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                "&response_type=code" +
                $"&scope={Uri.EscapeDataString(scope)}" +
                $"&state={Uri.EscapeDataString(state)}";

            return Redirect(url);
        }

        [HttpGet("callback")]
        public async Task<IActionResult> Callback(
            [FromQuery] string code,
            [FromQuery] string realmId,
            [FromQuery] string state)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(realmId))
            {
                return BadRequest("QuickBooks no devolvió code o realmId.");
            }

            string clientId = _config["QuickBooks:ClientId"];
            string clientSecret = _config["QuickBooks:ClientSecret"];
            string redirectUri = _config["QuickBooks:RedirectUri"];

            var client = _httpClientFactory.CreateClient();

            string basicAuth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")
            );

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", basicAuth);

            var form = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", redirectUri }
            };

            var response = await client.PostAsync(
                "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer",
                new FormUrlEncodedContent(form)
            );

            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return BadRequest("Error conectando QuickBooks: " + json);
            }

            var token = JsonSerializer.Deserialize<QuickBooksTokenResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (token == null)
            {
                return BadRequest("Respuesta inválida de QuickBooks.");
            }

            var existing = _db.QuickBooksConnections
                .FirstOrDefault(x => x.RealmId == realmId);

            if (existing == null)
            {
                existing = new QuickBooksConnection
                {
                    RealmId = realmId,
                    Environment = "production",
                    CreatedAt = DateTime.UtcNow
                };

                _db.QuickBooksConnections.Add(existing);
            }

            existing.AccessToken = token.AccessToken;
            existing.RefreshToken = token.RefreshToken;
            existing.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
            existing.RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(token.RefreshTokenExpiresIn);
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Content(
                "QuickBooks conectado correctamente al nuevo sistema QBTicketsApi.",
                "text/plain"
            );
        }

        private class QuickBooksTokenResponse
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public int ExpiresIn { get; set; }
            public int RefreshTokenExpiresIn { get; set; }
        }
    }
}