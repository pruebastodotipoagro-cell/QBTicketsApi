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

            string companyResult;
            string simpleQueryResult;
            string enhancedQueryResult;

            // 1. Prueba autenticada simple
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

            // 2. Query mínima
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

            // 3. Misma query + enhancedAllCustomFields
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

                companyInfo =
                    companyResult,

                simpleSalesReceiptQuery =
                    simpleQueryResult,

                enhancedSalesReceiptQuery =
                    enhancedQueryResult
            });
        }
    }
}