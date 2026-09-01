using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace QBTicketsApi.Controllers
{
    [ApiController]
    [Route("api/network-diagnostic")]
    public class NetworkDiagnosticController : ControllerBase
    {
        private readonly AppDbContext _db;

        public NetworkDiagnosticController(
            AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Test()
        {
            var connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                return Ok(new
                {
                    error =
                        "No existe conexión de QuickBooks."
                });
            }

            string noAuthResult;
            string companyResult;
            string simpleQueryResult;
            string enhancedQueryResult;

            // 1. Prueba al endpoint de QuickBooks SIN token
            try
            {
                using var handlerNoAuth =
                    new SocketsHttpHandler
                    {
                        ConnectTimeout =
                            TimeSpan.FromSeconds(10)
                    };

                using var clientNoAuth =
                    new HttpClient(handlerNoAuth)
                    {
                        Timeout =
                            TimeSpan.FromSeconds(15)
                    };

                clientNoAuth.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/json"
                    )
                );

                string url =
                    $"https://quickbooks.api.intuit.com/" +
                    $"v3/company/{connection.RealmId}/" +
                    $"companyinfo/{connection.RealmId}";

                var sw =
                    Stopwatch.StartNew();

                using var response =
                    await clientNoAuth.GetAsync(url);

                sw.Stop();

                string body =
                    await response.Content
                        .ReadAsStringAsync();

                noAuthResult =
                    $"HTTP {(int)response.StatusCode} " +
                    $"{response.StatusCode} " +
                    $"{sw.ElapsedMilliseconds}ms";

                if (!response.IsSuccessStatusCode)
                {
                    noAuthResult +=
                        " | " +
                        body.Substring(
                            0,
                            Math.Min(
                                body.Length,
                                300
                            )
                        );
                }
            }
            catch (Exception ex)
            {
                noAuthResult =
                    $"ERROR {ex.GetType().Name}: " +
                    ex.Message;
            }

            // Cliente autenticado con el token real
            using var handler =
                new SocketsHttpHandler
                {
                    ConnectTimeout =
                        TimeSpan.FromSeconds(10)
                };

            using var client =
                new HttpClient(handler)
                {
                    Timeout =
                        TimeSpan.FromSeconds(20)
                };

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    connection.AccessToken
                );

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"
                )
            );

            // 2. Prueba autenticada simple
            try
            {
                string url =
                    $"https://quickbooks.api.intuit.com/" +
                    $"v3/company/{connection.RealmId}/" +
                    $"companyinfo/{connection.RealmId}";

                var sw =
                    Stopwatch.StartNew();

                using var response =
                    await client.GetAsync(url);

                sw.Stop();

                string body =
                    await response.Content
                        .ReadAsStringAsync();

                companyResult =
                    $"HTTP {(int)response.StatusCode} " +
                    $"{response.StatusCode} " +
                    $"{sw.ElapsedMilliseconds}ms";

                if (!response.IsSuccessStatusCode)
                {
                    companyResult +=
                        " | " +
                        body.Substring(
                            0,
                            Math.Min(
                                body.Length,
                                300
                            )
                        );
                }
            }
            catch (Exception ex)
            {
                companyResult =
                    $"ERROR {ex.GetType().Name}: " +
                    ex.Message;
            }

            // 3. Query mínima
            try
            {
                string query =
                    Uri.EscapeDataString(
                        "SELECT * FROM SalesReceipt MAXRESULTS 1"
                    );

                string url =
                    $"https://quickbooks.api.intuit.com/" +
                    $"v3/company/{connection.RealmId}/query" +
                    $"?query={query}";

                var sw =
                    Stopwatch.StartNew();

                using var response =
                    await client.GetAsync(url);

                sw.Stop();

                string body =
                    await response.Content
                        .ReadAsStringAsync();

                simpleQueryResult =
                    $"HTTP {(int)response.StatusCode} " +
                    $"{response.StatusCode} " +
                    $"{sw.ElapsedMilliseconds}ms";

                if (!response.IsSuccessStatusCode)
                {
                    simpleQueryResult +=
                        " | " +
                        body.Substring(
                            0,
                            Math.Min(
                                body.Length,
                                300
                            )
                        );
                }
            }
            catch (Exception ex)
            {
                simpleQueryResult =
                    $"ERROR {ex.GetType().Name}: " +
                    ex.Message;
            }

            // 4. Misma query + enhancedAllCustomFields
            try
            {
                string query =
                    Uri.EscapeDataString(
                        "SELECT * FROM SalesReceipt MAXRESULTS 1"
                    );

                string url =
                    $"https://quickbooks.api.intuit.com/" +
                    $"v3/company/{connection.RealmId}/query" +
                    $"?query={query}" +
                    $"&include=enhancedAllCustomFields";

                var sw =
                    Stopwatch.StartNew();

                using var response =
                    await client.GetAsync(url);

                sw.Stop();

                string body =
                    await response.Content
                        .ReadAsStringAsync();

                enhancedQueryResult =
                    $"HTTP {(int)response.StatusCode} " +
                    $"{response.StatusCode} " +
                    $"{sw.ElapsedMilliseconds}ms";

                if (!response.IsSuccessStatusCode)
                {
                    enhancedQueryResult +=
                        " | " +
                        body.Substring(
                            0,
                            Math.Min(
                                body.Length,
                                300
                            )
                        );
                }
            }
            catch (Exception ex)
            {
                enhancedQueryResult =
                    $"ERROR {ex.GetType().Name}: " +
                    ex.Message;
            }

            return Ok(new
            {
                tokenExpiresAt =
                    connection.AccessTokenExpiresAt,

                companyInfoWithoutAuthorization =
                    noAuthResult,

                companyInfo =
                    companyResult,

                simpleSalesReceiptQuery =
                    simpleQueryResult,

                enhancedSalesReceiptQuery =
                    enhancedQueryResult
            });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            var connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No existe conexión de QuickBooks."
                });
            }

            string clientId =
                (HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>()
                    ["QuickBooks:ClientId"] ?? "")
                    .Trim();

            string clientSecret =
                (HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>()
                    ["QuickBooks:ClientSecret"] ?? "")
                    .Trim();

            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Falta ClientId o ClientSecret."
                });
            }

            using var client =
                new HttpClient
                {
                    Timeout =
                        TimeSpan.FromSeconds(20)
                };

            string basicAuth =
                Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{clientId}:{clientSecret}"
                    )
                );

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Basic",
                    basicAuth
                );

            var form =
                new Dictionary<string, string>
                {
            {
                "grant_type",
                "refresh_token"
            },
            {
                "refresh_token",
                connection.RefreshToken
            }
                };

            try
            {
                using var response =
                    await client.PostAsync(
                        "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer",
                        new FormUrlEncodedContent(form)
                    );

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                return Ok(new
                {
                    success =
                        response.IsSuccessStatusCode,

                    httpStatus =
                        (int)response.StatusCode,

                    status =
                        response.StatusCode.ToString(),

                    response =
                        json.Substring(
                            0,
                            Math.Min(
                                json.Length,
                                500
                            )
                        )
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    error =
                        ex.GetType().Name,
                    message =
                        ex.Message
                });
            }
        }
    }
}