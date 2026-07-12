using QBTicketsApi.Database;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QBTicketsApi.DTOs;

namespace QBTicketsApi.Services
{
    public class QuickBooksService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly CustomerLookupService _customerLookupService;

        public QuickBooksService(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config, CustomerLookupService customerLookupService)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _customerLookupService = customerLookupService;
        }

        // fechaDesde / fechaHasta en formato "yyyy-MM-dd". Si vienen null/vacíos, no se filtra por fecha.
        public async Task<string> GetSalesReceipts(string? fechaDesde = null, string? fechaHasta = null)
        {
            var connection = _db.QuickBooksConnections.FirstOrDefault();
            if (connection == null) return "No hay conexión QuickBooks.";

            if (connection.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
                await RefreshToken();

            connection = _db.QuickBooksConnections.FirstOrDefault();

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", connection.AccessToken);

            string whereClause = BuildDateWhereClause(fechaDesde, fechaHasta);
            string queryText = $"SELECT * FROM SalesReceipt{whereClause} MAXRESULTS 200";

            string query = Uri.EscapeDataString(queryText);
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

        // fechaDesde / fechaHasta en formato "yyyy-MM-dd". Si vienen null/vacíos, no se filtra por fecha.
        public async Task<string> GetCreditInvoices(string? fechaDesde = null, string? fechaHasta = null)
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

            string whereClause = BuildDateWhereClause(fechaDesde, fechaHasta);
            string queryText = $"SELECT * FROM Invoice{whereClause} MAXRESULTS 200";

            string query = Uri.EscapeDataString(queryText);
            string url = $"https://quickbooks.api.intuit.com/v3/company/{connection.RealmId}/query?query={query}";

            var response = await client.GetAsync(url);

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<InvoiceResponseDto>> GetCreditInvoicesList(string? fechaDesde = null, string? fechaHasta = null)
        {
            var json = await GetCreditInvoices(fechaDesde, fechaHasta);

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

                // Si esta factura ya fue certificada, usamos el NIT real con el que se certificó
                // (el que el cajero corrigió, si aplicó), no el del lookup automático.
                var certificada = _db.Invoices.FirstOrDefault(x => x.QuickBooksId == id && x.IsCertified);
                string customerNit = certificada != null
                    ? certificada.CustomerNit
                    : _customerLookupService.GetNit(customerName);

                result.Add(new InvoiceResponseDto
                {
                    QbInvoiceId = id,
                    InvoiceNumber = docNumber,
                    CustomerName = customerName,
                    CustomerNit = customerNit,
                    IssueDate = issueDate,
                    Total = total,
                    Balance = balance,
                    SaleType = "credito"
                });
            }

            return result;
        }

        public async Task<List<CreditCustomerSummaryDto>> GetCreditSummaryList()
        {
            var invoices = await GetCreditInvoicesList();

            var summary = invoices
                .Where(x => x.Balance > 0)
                .GroupBy(x => x.CustomerName)
                .Select(g =>
                {
                    var last = g.OrderByDescending(x => x.IssueDate).First();

                    return new CreditCustomerSummaryDto
                    {
                        CustomerName = g.Key,
                        CustomerNit = _customerLookupService.GetNit(g.Key),
                        TotalDebt = g.Sum(x => x.Balance),
                        OpenInvoices = g.Count(),
                        LastInvoiceId = last.QbInvoiceId,
                        LastInvoiceNumber = last.InvoiceNumber
                    };
                })
                .OrderBy(x => x.CustomerName)
                .ToList();

            return summary;
        }

        public async Task<string> GetInvoiceById(string id)
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

            string query = Uri.EscapeDataString($"SELECT * FROM Invoice WHERE Id = '{id}'");
            string url = $"https://quickbooks.api.intuit.com/v3/company/{connection.RealmId}/query?query={query}";

            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        // Construye "WHERE TxnDate >= '...' AND TxnDate <= '...'" según lo que venga.
        // QBO espera fechas en formato yyyy-MM-dd dentro del query.
        private static string BuildDateWhereClause(string? fechaDesde, string? fechaHasta)
        {
            var condiciones = new List<string>();

            if (!string.IsNullOrWhiteSpace(fechaDesde) && DateTime.TryParse(fechaDesde, out var desde))
                condiciones.Add($"TxnDate >= '{desde:yyyy-MM-dd}'");

            if (!string.IsNullOrWhiteSpace(fechaHasta) && DateTime.TryParse(fechaHasta, out var hasta))
                condiciones.Add($"TxnDate <= '{hasta:yyyy-MM-dd}'");

            if (condiciones.Count == 0)
                return "";

            return " WHERE " + string.Join(" AND ", condiciones);
        }

        public async Task<InvoiceItemsResponseDto> GetDocumentItemsAsync(string id)
        {
            string json = await GetSalesReceiptById(id);
            string saleType = "contado";

            if (string.IsNullOrWhiteSpace(json) ||
                !json.Contains("\"SalesReceipt\""))
            {
                json = await GetInvoiceById(id);
                saleType = "credito";
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new Exception(
                    "No se encontró el documento en QuickBooks."
                );
            }

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(
                "QueryResponse",
                out var queryResponse))
            {
                throw new Exception(
                    "QuickBooks no devolvió QueryResponse."
                );
            }

            JsonElement documents;

            if (saleType == "contado")
            {
                if (!queryResponse.TryGetProperty(
                    "SalesReceipt",
                    out documents) ||
                    documents.GetArrayLength() == 0)
                {
                    throw new Exception(
                        "No se encontró el recibo de venta."
                    );
                }
            }
            else
            {
                if (!queryResponse.TryGetProperty(
                    "Invoice",
                    out documents) ||
                    documents.GetArrayLength() == 0)
                {
                    throw new Exception(
                        "No se encontró la factura."
                    );
                }
            }

            JsonElement qbDocument = documents[0];

            string quickBooksId =
                qbDocument.TryGetProperty("Id", out var idElement)
                    ? idElement.GetString() ?? id
                    : id;

            string invoiceNumber =
                qbDocument.TryGetProperty("DocNumber", out var docNumber)
                    ? docNumber.GetString() ?? quickBooksId
                    : quickBooksId;

            string customerName = "Consumidor Final";

            if (qbDocument.TryGetProperty(
                    "CustomerRef",
                    out var customerRef) &&
                customerRef.TryGetProperty(
                    "name",
                    out var customerNameElement))
            {
                customerName =
                    customerNameElement.GetString()
                    ?? "Consumidor Final";
            }

            var result = new InvoiceItemsResponseDto
            {
                QuickBooksId = quickBooksId,
                InvoiceNumber = invoiceNumber,
                CustomerName = customerName,
                SaleType = saleType
            };

            if (qbDocument.TryGetProperty(
                "Line",
                out var lines))
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (!line.TryGetProperty(
                        "DetailType",
                        out var detailTypeElement))
                    {
                        continue;
                    }

                    string detailType =
                        detailTypeElement.GetString() ?? "";

                    if (detailType != "SalesItemLineDetail")
                    {
                        continue;
                    }

                    if (!line.TryGetProperty(
                        "SalesItemLineDetail",
                        out var detail))
                    {
                        continue;
                    }

                    string lineId =
                        line.TryGetProperty("Id", out var lineIdElement)
                            ? lineIdElement.GetString() ?? ""
                            : "";

                    string description =
                        line.TryGetProperty(
                            "Description",
                            out var descriptionElement)
                            ? descriptionElement.GetString() ?? ""
                            : "";

                    string itemId = "";
                    string itemName = "";

                    if (detail.TryGetProperty(
                            "ItemRef",
                            out var itemRef))
                    {
                        itemId =
                            itemRef.TryGetProperty(
                                "value",
                                out var itemValue)
                                ? itemValue.GetString() ?? ""
                                : "";

                        itemName =
                            itemRef.TryGetProperty(
                                "name",
                                out var itemNameElement)
                                ? itemNameElement.GetString() ?? ""
                                : "";
                    }

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = itemName;
                    }

                    decimal quantity = 1m;

                    if (detail.TryGetProperty(
                            "Qty",
                            out var quantityElement))
                    {
                        quantityElement.TryGetDecimal(
                            out quantity
                        );
                    }

                    decimal unitPrice = 0m;

                    if (detail.TryGetProperty(
                            "UnitPrice",
                            out var unitPriceElement))
                    {
                        unitPriceElement.TryGetDecimal(
                            out unitPrice
                        );
                    }

                    decimal amount = 0m;

                    if (line.TryGetProperty(
                            "Amount",
                            out var amountElement))
                    {
                        amountElement.TryGetDecimal(
                            out amount
                        );
                    }

                    decimal currentDiscount = 0m;

                    if (detail.TryGetProperty(
                            "DiscountAmt",
                            out var discountElement))
                    {
                        discountElement.TryGetDecimal(
                            out currentDiscount
                        );
                    }

                    decimal subtotal =
                        quantity * unitPrice;

                    if (subtotal <= 0)
                    {
                        subtotal = amount + currentDiscount;
                    }

                    result.Items.Add(new InvoiceItemDto
                    {
                        LineId = lineId,
                        ItemId = itemId,
                        Description = description,
                        Quantity = quantity,
                        UnitPrice = unitPrice,
                        Subtotal = subtotal,
                        CurrentDiscount = currentDiscount,
                        Total = amount
                    });
                }
            }

            result.Subtotal =
                result.Items.Sum(x => x.Subtotal);

            result.DiscountTotal =
                result.Items.Sum(x => x.CurrentDiscount);

            result.Total =
                qbDocument.TryGetProperty(
                    "TotalAmt",
                    out var totalElement) &&
                totalElement.TryGetDecimal(out var total)
                    ? total
                    : result.Items.Sum(x => x.Total);

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