using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.DTOs.QBTicketsApi.DTOs;
using QBTicketsApi.Models;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBTicketsApi.Services
{
    public class QuickBooksService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly CustomerLookupService _customerLookupService;
        private readonly IMemoryCache _memoryCache;

        /*
         * Caché por solicitud HTTP. ReportsService y QuickBooksService
         * son servicios con el mismo alcance, por lo que las listas ya
         * descargadas de QuickBooks pueden reutilizar sus métodos de pago
         * y líneas sin volver a consultar documento por documento.
         */
        private readonly Dictionary<string, string>
            _reportPaymentMethodCache =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );

        private readonly Dictionary<string, InvoiceItemsResponseDto>
            _reportDocumentItemsCache =
                new Dictionary<string, InvoiceItemsResponseDto>(
                    StringComparer.OrdinalIgnoreCase
                );

        public QuickBooksService(
            AppDbContext db,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            CustomerLookupService customerLookupService,
            IMemoryCache memoryCache)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _customerLookupService = customerLookupService;
            _memoryCache = memoryCache;
        }

        private static string BuildCacheKey(
            string prefix,
            string? from = null,
            string? to = null)
        {
            return prefix + "|" +
                (from ?? "") + "|" +
                (to ?? "");
        }

        private static MemoryCacheEntryOptions ShortCache()
        {
            return new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(
                    TimeSpan.FromSeconds(20)
                );
        }

        private static MemoryCacheEntryOptions MediumCache()
        {
            return new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(
                    TimeSpan.FromMinutes(10)
                );
        }

        // fechaDesde / fechaHasta en formato "yyyy-MM-dd". Si vienen null/vacíos, no se filtra por fecha.
        public async Task<string> GetSalesReceipts(string? fechaDesde = null, string? fechaHasta = null)
        {
            string cacheKey =
                BuildCacheKey(
                    "qb-sales-receipts",
                    fechaDesde,
                    fechaHasta
                );

            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out string? cachedJson) &&
                !string.IsNullOrWhiteSpace(cachedJson))
            {
                return cachedJson;
            }

            var connection = _db.QuickBooksConnections.FirstOrDefault();

            if (connection == null)
            {
                throw new Exception("No hay conexión con QuickBooks.");
            }

            if (connection.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshToken();
            }

            connection = _db.QuickBooksConnections.FirstOrDefault();

            if (connection == null)
            {
                throw new Exception("No se pudo recuperar la conexión con QuickBooks.");
            }

            var client = _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    connection.AccessToken
                );

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"
                )
            );

            string whereClause =
                BuildDateWhereClause(
                    fechaDesde,
                    fechaHasta
                );

            string queryText =
                $"SELECT * FROM SalesReceipt{whereClause} MAXRESULTS 200";

            string query =
                Uri.EscapeDataString(queryText);

            string url =
                $"https://quickbooks.api.intuit.com/v3/company/" +
                $"{connection.RealmId}/query" +
                $"?query={query}" +
                $"&include=enhancedAllCustomFields";

            HttpResponseMessage response =
                await client.GetAsync(url);

            string responseText =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    "QuickBooks no pudo cargar los recibos de venta.\n" +
                    $"Código HTTP: {(int)response.StatusCode} " +
                    $"{response.StatusCode}\n" +
                    responseText
                );
            }

            string contentType =
                response.Content.Headers.ContentType?.MediaType ?? "";

            if (!contentType.Contains(
                "json",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "QuickBooks devolvió un formato distinto de JSON.\n" +
                    $"Content-Type: {contentType}\n" +
                    responseText
                );
            }

            _memoryCache.Set(
                cacheKey,
                responseText,
                ShortCache()
            );

            return responseText;
        }

        public async Task<List<InvoiceResponseDto>>
    GetSalesReceiptsList(
        string? fechaDesde = null,
        string? fechaHasta = null)
        {
            string json =
                await GetSalesReceipts(
                    fechaDesde,
                    fechaHasta
                );

            var result =
                new List<InvoiceResponseDto>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            using var doc =
                JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(
                "QueryResponse",
                out var queryResponse))
            {
                return result;
            }

            if (!queryResponse.TryGetProperty(
                "SalesReceipt",
                out var salesReceipts))
            {
                return result;
            }

            foreach (var receipt in salesReceipts.EnumerateArray())
            {
                string id =
                    receipt.TryGetProperty(
                        "Id",
                        out var idValue)
                        ? idValue.GetString() ?? ""
                        : "";

                string docNumber =
                    receipt.TryGetProperty(
                        "DocNumber",
                        out var docValue)
                        ? docValue.GetString() ?? id
                        : id;

                string cashierName =
                    GetCashierFromTransactionJson(receipt);

                string customerNameQuickBooks =
                    "Consumidor Final";

                if (receipt.TryGetProperty(
                        "CustomerRef",
                        out var customerRef) &&
                    customerRef.TryGetProperty(
                        "name",
                        out var nameValue))
                {
                    customerNameQuickBooks =
                        nameValue.GetString()
                        ?? "Consumidor Final";
                }

                DateTime issueDate =
                    DateTime.UtcNow;

                if (receipt.TryGetProperty(
                        "TxnDate",
                        out var dateValue))
                {
                    DateTime.TryParse(
                        dateValue.GetString(),
                        out issueDate
                    );
                }

                decimal totalQuickBooks = 0;

                if (receipt.TryGetProperty(
                        "TotalAmt",
                        out var totalValue))
                {
                    totalValue.TryGetDecimal(
                        out totalQuickBooks
                    );
                }

                CacheReportDocumentData(
                    receipt,
                    id,
                    docNumber,
                    customerNameQuickBooks,
                    "contado"
                );

                var certificada =
                    await _db.Invoices
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.QuickBooksId == id &&
                                x.IsCertified
                        );

                string customerNitFinal;
                string customerNameFinal;
                decimal totalFinal;

                if (certificada != null)
                {
                    customerNitFinal =
                        string.IsNullOrWhiteSpace(
                            certificada.CustomerNit
                        )
                            ? "CF"
                            : certificada.CustomerNit;

                    customerNameFinal =
                        string.IsNullOrWhiteSpace(
                            certificada.CustomerName
                        )
                            ? "Consumidor Final"
                            : certificada.CustomerName;

                    totalFinal =
                        certificada.Total;
                }
                else
                {
                    customerNitFinal =
                        _customerLookupService.GetNit(
                            customerNameQuickBooks
                        );

                    if (string.IsNullOrWhiteSpace(
                        customerNitFinal))
                    {
                        customerNitFinal = "CF";
                    }

                    customerNameFinal =
                        customerNameQuickBooks;

                    totalFinal =
                        totalQuickBooks;
                }

                result.Add(
                    new InvoiceResponseDto
                    {
                        QbInvoiceId = id,
                        InvoiceNumber = docNumber,
                        CustomerName = customerNameFinal,
                        CustomerNit = customerNitFinal,
                        IssueDate = issueDate,
                        Total = totalFinal,
                        Balance = 0,
                        SaleType = "contado",
                        CashierName = cashierName
                    }
                );
            }

            return result
                .OrderByDescending(
                    x => x.IssueDate
                )
                .ToList();
        }


        public string GetCachedPaymentMethod(
            string quickBooksId,
            string saleType)
        {
            if (string.Equals(
                    saleType,
                    "credito",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Crédito";
            }

            if (!string.IsNullOrWhiteSpace(quickBooksId) &&
                _reportPaymentMethodCache.TryGetValue(
                    quickBooksId,
                    out string? paymentMethod) &&
                !string.IsNullOrWhiteSpace(paymentMethod))
            {
                return paymentMethod;
            }

            return "No indicado";
        }

        private void CacheReportDocumentData(
            JsonElement document,
            string quickBooksId,
            string invoiceNumber,
            string customerName,
            string saleType)
        {
            if (string.IsNullOrWhiteSpace(quickBooksId))
            {
                return;
            }

            string paymentMethod =
                string.Equals(
                    saleType,
                    "credito",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Crédito"
                    : GetPaymentMethodFromTransaction(
                        document
                    );

            _reportPaymentMethodCache[
                quickBooksId
            ] =
                paymentMethod;

            var result =
                new InvoiceItemsResponseDto
                {
                    QuickBooksId =
                        quickBooksId,

                    InvoiceNumber =
                        invoiceNumber,

                    CustomerName =
                        string.IsNullOrWhiteSpace(
                            customerName)
                            ? "Consumidor Final"
                            : customerName,

                    SaleType =
                        saleType
                };

            if (document.TryGetProperty(
                    "Line",
                    out JsonElement lines) &&
                lines.ValueKind ==
                    JsonValueKind.Array)
            {
                foreach (
                    JsonElement line
                    in lines.EnumerateArray())
                {
                    if (!line.TryGetProperty(
                            "DetailType",
                            out JsonElement detailTypeElement) ||
                        !string.Equals(
                            detailTypeElement.GetString(),
                            "SalesItemLineDetail",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!line.TryGetProperty(
                            "SalesItemLineDetail",
                            out JsonElement detail))
                    {
                        continue;
                    }

                    string lineId =
                        line.TryGetProperty(
                            "Id",
                            out JsonElement lineIdElement)
                            ? lineIdElement.GetString() ?? ""
                            : "";

                    string itemId = "";
                    string itemName = "";

                    if (detail.TryGetProperty(
                            "ItemRef",
                            out JsonElement itemRef))
                    {
                        itemId =
                            itemRef.TryGetProperty(
                                "value",
                                out JsonElement itemValue)
                                ? itemValue.GetString() ?? ""
                                : "";

                        itemName =
                            itemRef.TryGetProperty(
                                "name",
                                out JsonElement itemNameElement)
                                ? itemNameElement.GetString() ?? ""
                                : "";
                    }

                    string description =
                        !string.IsNullOrWhiteSpace(itemName)
                            ? itemName
                            : line.TryGetProperty(
                                "Description",
                                out JsonElement descriptionElement)
                                ? descriptionElement.GetString() ?? ""
                                : "";

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = "Producto";
                    }

                    decimal quantity = 1m;

                    if (detail.TryGetProperty(
                            "Qty",
                            out JsonElement quantityElement))
                    {
                        quantityElement.TryGetDecimal(
                            out quantity
                        );
                    }

                    decimal unitPrice = 0m;

                    if (detail.TryGetProperty(
                            "UnitPrice",
                            out JsonElement unitPriceElement))
                    {
                        unitPriceElement.TryGetDecimal(
                            out unitPrice
                        );
                    }

                    decimal amount = 0m;

                    if (line.TryGetProperty(
                            "Amount",
                            out JsonElement amountElement))
                    {
                        amountElement.TryGetDecimal(
                            out amount
                        );
                    }

                    decimal currentDiscount = 0m;

                    if (detail.TryGetProperty(
                            "DiscountAmt",
                            out JsonElement discountElement))
                    {
                        discountElement.TryGetDecimal(
                            out currentDiscount
                        );
                    }

                    decimal subtotal =
                        quantity * unitPrice;

                    if (subtotal <= 0m)
                    {
                        subtotal =
                            amount +
                            currentDiscount;
                    }

                    result.Items.Add(
                        new InvoiceItemDto
                        {
                            LineId =
                                lineId,

                            ItemId =
                                itemId,

                            Description =
                                description,

                            Quantity =
                                quantity,

                            UnitPrice =
                                unitPrice,

                            Subtotal =
                                subtotal,

                            CurrentDiscount =
                                currentDiscount,

                            Total =
                                amount
                        }
                    );
                }
            }

            result.Subtotal =
                result.Items.Sum(
                    x => x.Subtotal
                );

            result.DiscountTotal =
                result.Items.Sum(
                    x => x.CurrentDiscount
                );

            result.Total =
                document.TryGetProperty(
                    "TotalAmt",
                    out JsonElement totalElement) &&
                totalElement.TryGetDecimal(
                    out decimal total)
                    ? total
                    : result.Items.Sum(
                        x => x.Total
                    );

            _reportDocumentItemsCache[
                quickBooksId
            ] =
                result;
        }

        private static string GetPaymentMethodFromTransaction(
            JsonElement document)
        {
            if (document.TryGetProperty(
                    "PaymentMethodRef",
                    out JsonElement paymentMethodRef) &&
                paymentMethodRef.TryGetProperty(
                    "name",
                    out JsonElement paymentMethodName))
            {
                string name =
                    paymentMethodName.GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(name))
                {
                    return NormalizePaymentMethodForReport(
                        name
                    );
                }
            }

            if (document.TryGetProperty(
                    "DepositToAccountRef",
                    out JsonElement depositRef) &&
                depositRef.TryGetProperty(
                    "name",
                    out JsonElement depositName))
            {
                string account =
                    depositName.GetString() ?? "";

                return NormalizePaymentMethodForReport(
                    account
                );
            }

            return "No indicado";
        }

        private static string NormalizePaymentMethodForReport(
            string value)
        {
            value =
                value ?? "";

            if (value.Contains(
                "efect",
                StringComparison.OrdinalIgnoreCase) ||
                value.Contains(
                "caja",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Efectivo";
            }

            if (value.Contains(
                "cheque",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Cheque";
            }

            if (value.Contains(
                    "tarjeta",
                    StringComparison.OrdinalIgnoreCase) ||
                value.Contains(
                    "credit",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Tarjeta de crédito";
            }

            if (value.Contains(
                "transfer",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Transferencia";
            }

            return string.IsNullOrWhiteSpace(value)
                ? "No indicado"
                : value;
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
            string cacheKey =
                "qb-sales-receipt|" + id;

            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out string? cachedJson) &&
                !string.IsNullOrWhiteSpace(cachedJson))
            {
                return cachedJson;
            }

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
            string url =
                $"https://quickbooks.api.intuit.com/v3/company/" +
                $"{connection.RealmId}/query" +
                $"?query={query}" +
                $"&include=enhancedAllCustomFields";

            var response = await client.GetAsync(url);
            string responseText =
                await response.Content.ReadAsStringAsync();

            _memoryCache.Set(
                cacheKey,
                responseText,
                ShortCache()
            );

            return responseText;
        }

        // fechaDesde / fechaHasta en formato "yyyy-MM-dd". Si vienen null/vacíos, no se filtra por fecha.
        public async Task<string> GetCreditInvoices(string? fechaDesde = null, string? fechaHasta = null)
        {
            string cacheKey =
                BuildCacheKey(
                    "qb-credit-invoices",
                    fechaDesde,
                    fechaHasta
                );

            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out string? cachedJson) &&
                !string.IsNullOrWhiteSpace(cachedJson))
            {
                return cachedJson;
            }

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

            string url =
                $"https://quickbooks.api.intuit.com/v3/company/" +
                $"{connection.RealmId}/query" +
                $"?query={query}" +
                $"&include=enhancedAllCustomFields";

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

            var customerNitCache =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var inv in invoices.EnumerateArray())
            {
                string id = inv.TryGetProperty("Id", out var idValue) ? idValue.GetString() ?? "" : "";
                string docNumber = inv.TryGetProperty("DocNumber", out var docValue) ? docValue.GetString() ?? "" : id;

                string cashierName =
                    GetCashierFromTransactionJson(inv);

                string customerName =
                    "Consumidor Final";

                string customerId =
                    "";

                if (inv.TryGetProperty(
                        "CustomerRef",
                        out var customerRef))
                {
                    customerName =
                        customerRef.TryGetProperty(
                            "name",
                            out var nameValue)
                            ? nameValue.GetString()
                                ?? customerName
                            : customerName;

                    customerId =
                        customerRef.TryGetProperty(
                            "value",
                            out var customerIdValue)
                            ? customerIdValue.GetString()
                                ?? ""
                            : "";
                }

                DateTime issueDate = DateTime.UtcNow;
                if (inv.TryGetProperty("TxnDate", out var dateValue))
                    DateTime.TryParse(dateValue.GetString(), out issueDate);

                decimal total = 0;
                if (inv.TryGetProperty("TotalAmt", out var totalValue))
                    totalValue.TryGetDecimal(out total);

                decimal balance = 0;
                if (inv.TryGetProperty("Balance", out var balanceValue))
                    balanceValue.TryGetDecimal(out balance);

                CacheReportDocumentData(
                    inv,
                    id,
                    docNumber,
                    customerName,
                    "credito"
                );

                // Si esta factura ya fue certificada, usamos el NIT real con el que se certificó
                // (el que el cajero corrigió, si aplicó), no el del lookup automático.
                var certificada = await _db.Invoices
     .AsNoTracking()
     .FirstOrDefaultAsync(
         x => x.QuickBooksId == id &&
              x.IsCertified
     );

                string customerNit;
                string customerNameFinal;
                decimal totalFinal;

                if (certificada != null)
                {
                    customerNit =
                        string.IsNullOrWhiteSpace(certificada.CustomerNit)
                            ? "CF"
                            : certificada.CustomerNit;

                    customerNameFinal =
                        string.IsNullOrWhiteSpace(certificada.CustomerName)
                            ? "Consumidor Final"
                            : certificada.CustomerName;

                    totalFinal = certificada.Total;
                }
                else
                {
                    customerNit =
                        "";

                    if (!string.IsNullOrWhiteSpace(
                        customerId))
                    {
                        if (!customerNitCache.TryGetValue(
                                customerId,
                                out customerNit))
                        {
                            customerNit =
                                await GetCustomerNitForInvoiceAsync(
                                    customerId
                                );

                            customerNitCache[
                                customerId
                            ] =
                                customerNit;
                        }
                    }

                    /*
                     * Como respaldo conservamos el lookup anterior.
                     * La fuente principal ahora es el campo
                     * personalizado NIT del cliente en QuickBooks.
                     */
                    if (string.IsNullOrWhiteSpace(
                            customerNit) ||
                        customerNit.Equals(
                            "CF",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        string lookupNit =
                            _customerLookupService.GetNit(
                                customerName
                            );

                        if (!string.IsNullOrWhiteSpace(
                            lookupNit))
                        {
                            customerNit =
                                lookupNit;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(
                        customerNit))
                    {
                        customerNit =
                            "CF";
                    }

                    customerNameFinal =
                        customerName;

                    totalFinal =
                        total;
                }

                result.Add(new InvoiceResponseDto
                {
                    QbInvoiceId = id,
                    InvoiceNumber = docNumber,
                    CustomerName = customerNameFinal,
                    CustomerNit = customerNit,
                    Total = totalFinal,
                    IssueDate = issueDate,
                    Balance = balance,
                    SaleType = "credito",
                    CashierName = cashierName
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
            string cacheKey =
                "qb-invoice|" + id;

            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out string? cachedJson) &&
                !string.IsNullOrWhiteSpace(cachedJson))
            {
                return cachedJson;
            }

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

            string query =
                Uri.EscapeDataString(
                    $"SELECT * FROM Invoice WHERE Id = '{id}'"
                );

            string url =
                $"https://quickbooks.api.intuit.com/v3/company/" +
                $"{connection.RealmId}/query" +
                $"?query={query}" +
                $"&include=enhancedAllCustomFields";

            var response = await client.GetAsync(url);
            string responseText =
                await response.Content.ReadAsStringAsync();

            _memoryCache.Set(
                cacheKey,
                responseText,
                ShortCache()
            );

            return responseText;
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
            if (!string.IsNullOrWhiteSpace(id) &&
                _reportDocumentItemsCache.TryGetValue(
                    id,
                    out InvoiceItemsResponseDto? cachedItems))
            {
                return cachedItems;
            }

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

                    string description = "";
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

                    description = itemName;

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description =
                            line.TryGetProperty(
                                "Description",
                                out var descriptionElement)
                                ? descriptionElement.GetString() ?? ""
                                : "";
                    }

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = "Producto";
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
        private async Task<string>
            GetCustomerNitForInvoiceAsync(
                string customerId)
        {
            if (string.IsNullOrWhiteSpace(
                customerId))
            {
                return "CF";
            }

            try
            {
                string customerJson =
                    await GetCustomerByIdAsync(
                        customerId
                    );

                if (string.IsNullOrWhiteSpace(
                    customerJson))
                {
                    return "CF";
                }

                using JsonDocument document =
                    JsonDocument.Parse(
                        customerJson
                    );

                if (!document.RootElement
                    .TryGetProperty(
                        "QueryResponse",
                        out JsonElement queryResponse))
                {
                    return "CF";
                }

                if (!queryResponse.TryGetProperty(
                        "Customer",
                        out JsonElement customers) ||
                    customers.ValueKind !=
                        JsonValueKind.Array ||
                    customers.GetArrayLength() == 0)
                {
                    return "CF";
                }

                return GetNitFromCustomerJson(
                    customers[0]
                );
            }
            catch
            {
                /*
                 * No bloqueamos la carga del dashboard
                 * si QuickBooks no devuelve el cliente.
                 */
                return "CF";
            }
        }

        public async Task<string> GetCustomerByIdAsync(
    string customerId)
        {
            string cacheKey =
                "qb-customer|" + customerId;

            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out string? cachedJson) &&
                !string.IsNullOrWhiteSpace(cachedJson))
            {
                return cachedJson;
            }

            if (string.IsNullOrWhiteSpace(customerId))
            {
                throw new Exception(
                    "El ID del cliente está vacío."
                );
            }

            var connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No hay conexión con QuickBooks."
                );
            }

            if (connection.AccessTokenExpiresAt <=
                DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshToken();
            }

            connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No se pudo recuperar la conexión con QuickBooks."
                );
            }

            var client =
                _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    connection.AccessToken
                );

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"
                )
            );

            string queryText =
                $"SELECT * FROM Customer " +
                $"WHERE Id = '{customerId.Trim()}'";

            string query =
                Uri.EscapeDataString(queryText);

            string url =
                $"https://quickbooks.api.intuit.com/v3/company/" +
                $"{connection.RealmId}/query" +
                $"?query={query}" +
                $"&include=enhancedAllCustomFields";

            HttpResponseMessage response =
                await client.GetAsync(url);

            string responseText =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    "No se pudo consultar el cliente en QuickBooks.\n" +
                    $"Código HTTP: {(int)response.StatusCode}\n" +
                    responseText
                );
            }

            _memoryCache.Set(
                cacheKey,
                responseText,
                MediumCache()
            );

            return responseText;
        }

        public async Task<List<QuickBooksCustomerDto>>
    GetCustomersListAsync()
        {
            var connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No hay conexión con QuickBooks."
                );
            }

            if (connection.AccessTokenExpiresAt <=
                DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshToken();
            }

            connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No se pudo recuperar la conexión con QuickBooks."
                );
            }

            var client =
                _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    connection.AccessToken
                );

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"
                )
            );

            var result =
                new List<QuickBooksCustomerDto>();

            int startPosition = 1;
            const int maxResults = 1000;

            while (true)
            {
                string queryText =
                    "SELECT * FROM Customer " +
                    $"STARTPOSITION {startPosition} " +
                    $"MAXRESULTS {maxResults}";

                string query =
                    Uri.EscapeDataString(queryText);

                string url =
                    $"https://quickbooks.api.intuit.com/v3/company/" +
                    $"{connection.RealmId}/query" +
                    $"?query={query}" +
                    $"&include=enhancedAllCustomFields";

                HttpResponseMessage response =
                    await client.GetAsync(url);

                string responseText =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        "No se pudieron consultar los clientes de QuickBooks.\n" +
                        $"Código HTTP: {(int)response.StatusCode}\n" +
                        responseText
                    );
                }

                using JsonDocument doc =
                    JsonDocument.Parse(responseText);

                if (!doc.RootElement.TryGetProperty(
                        "QueryResponse",
                        out JsonElement queryResponse))
                {
                    break;
                }

                if (!queryResponse.TryGetProperty(
                        "Customer",
                        out JsonElement customers) ||
                    customers.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                int cantidadPagina =
                    customers.GetArrayLength();

                foreach (
                    JsonElement customer
                    in customers.EnumerateArray())
                {
                    string customerId =
                        customer.TryGetProperty(
                            "Id",
                            out JsonElement idElement)
                            ? idElement.GetString() ?? ""
                            : "";

                    string displayName =
                        customer.TryGetProperty(
                            "DisplayName",
                            out JsonElement nameElement)
                            ? nameElement.GetString() ?? ""
                            : "";

                    bool active =
                        customer.TryGetProperty(
                            "Active",
                            out JsonElement activeElement) &&
                        activeElement.ValueKind ==
                            JsonValueKind.True;

                    string nit =
                        GetNitFromCustomerJson(customer);

                    string phone = "";

                    if (customer.TryGetProperty(
                            "PrimaryPhone",
                            out JsonElement primaryPhone))
                    {
                        phone =
                            primaryPhone.TryGetProperty(
                                "FreeFormNumber",
                                out JsonElement phoneElement)
                                ? phoneElement.GetString() ?? ""
                                : "";
                    }

                    string address = "";

                    if (customer.TryGetProperty(
                            "BillAddr",
                            out JsonElement billAddress))
                    {
                        address =
                            billAddress.TryGetProperty(
                                "Line1",
                                out JsonElement addressElement)
                                ? addressElement.GetString() ?? ""
                                : "";
                    }

                    result.Add(
                        new QuickBooksCustomerDto
                        {
                            CustomerId = customerId,
                            DisplayName = displayName,
                            Nit = nit,
                            Phone = phone,
                            Address = address,
                            Active = active
                        }
                    );
                }

                if (cantidadPagina < maxResults)
                {
                    break;
                }

                startPosition += maxResults;
            }

            return result
                .Where(x => x.Active)
                .OrderBy(x => x.DisplayName)
                .ToList();
        }

        private static string GetNitFromCustomerJson(
            JsonElement customer)
        {
            if (!customer.TryGetProperty(
                    "CustomField",
                    out JsonElement customFields) ||
                customFields.ValueKind != JsonValueKind.Array)
            {
                return "CF";
            }

            foreach (
                JsonElement field
                in customFields.EnumerateArray())
            {
                string fieldName =
                    field.TryGetProperty(
                        "Name",
                        out JsonElement nameElement)
                        ? nameElement.GetString() ?? ""
                        : "";

                if (!fieldName.Equals(
                        "NIT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string nit =
                    field.TryGetProperty(
                        "StringValue",
                        out JsonElement valueElement)
                        ? valueElement.GetString() ?? ""
                        : "";

                nit =
                    nit.Trim()
                        .Replace("-", "")
                        .Replace(" ", "");

                return string.IsNullOrWhiteSpace(nit)
                    ? "CF"
                    : nit;
            }

            return "CF";
        }

        private static string GetCashierFromTransactionJson(
    JsonElement transaction)
        {
            if (!transaction.TryGetProperty(
                    "CustomField",
                    out JsonElement customFields) ||
                customFields.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            foreach (JsonElement field in customFields.EnumerateArray())
            {
                string fieldName =
                    field.TryGetProperty(
                        "Name",
                        out JsonElement nameElement)
                        ? nameElement.GetString() ?? ""
                        : "";

                if (!fieldName.Equals(
                        "CAJERO",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string optionId =
                    field.TryGetProperty(
                        "StringValue",
                        out JsonElement valueElement)
                        ? valueElement.GetString() ?? ""
                        : "";

                return GetCashierNameFromOptionId(
                    optionId.Trim()
                );
            }

            return "";
        }

        private static string GetCashierNameFromOptionId(
            string optionId)
        {
            string clean =
                NormalizeCashierKey(
                    optionId
                );

            /*
             * QuickBooks puede devolver el ID de la opción
             * o el texto visible del campo personalizado.
             */
            if (clean == "1" ||
                clean == "ROCIO" ||
                clean == "ROCIO RAMOS")
            {
                return "ROCIO RAMOS";
            }

            if (clean == "2" ||
                clean == "ADAN" ||
                clean == "ADAN HERNANDEZ")
            {
                return "ADAN HERNANDEZ";
            }

            if (clean == "3" ||
                clean == "FERNANDO" ||
                clean == "FERNANDO GOMEZ")
            {
                return "FERNANDO GOMEZ";
            }

            if (clean == "4" ||
                clean == "CARLOS" ||
                clean == "CARLOS LORENZANA")
            {
                return "CARLOS LORENZANA";
            }

            if (clean == "5" ||
                clean == "PAOLA" ||
                clean == "PAOLA VALLADARES")
            {
                return "PAOLA VALLADARES";
            }

            return clean;
        }

        private static string NormalizeCashierKey(
            string value)
        {
            string normalized =
                (value ?? "")
                    .Trim()
                    .ToUpperInvariant()
                    .Normalize(
                        NormalizationForm.FormD
                    );

            var builder =
                new StringBuilder();

            foreach (char character in normalized)
            {
                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(
                        character
                    );

                if (category !=
                    UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(
                        character
                    );
                }
            }

            return builder
                .ToString()
                .Normalize(
                    NormalizationForm.FormC
                )
                .Replace(".", " ")
                .Replace(",", " ")
                .Replace("-", " ")
                .Replace("  ", " ")
                .Trim();
        }

        public async Task<string> GetDocumentCashierNameAsync(
    string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "";
            }

            string json =
                await GetSalesReceiptById(id);

            if (!string.IsNullOrWhiteSpace(json) &&
                json.Contains(
                    "\"SalesReceipt\"",
                    StringComparison.Ordinal))
            {
                return GetCashierFromQuickBooksResponse(
                    json,
                    "SalesReceipt"
                );
            }

            json =
                await GetInvoiceById(id);

            if (!string.IsNullOrWhiteSpace(json) &&
                json.Contains(
                    "\"Invoice\"",
                    StringComparison.Ordinal))
            {
                return GetCashierFromQuickBooksResponse(
                    json,
                    "Invoice"
                );
            }

            return "";
        }

        private static string GetCashierFromQuickBooksResponse(
            string json,
            string documentProperty)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "";
            }

            using JsonDocument doc =
                JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(
                    "QueryResponse",
                    out JsonElement queryResponse))
            {
                return "";
            }

            if (!queryResponse.TryGetProperty(
                    documentProperty,
                    out JsonElement documents) ||
                documents.ValueKind != JsonValueKind.Array ||
                documents.GetArrayLength() == 0)
            {
                return "";
            }

            return GetCashierFromTransactionJson(
                documents[0]
            );
        }
        private static string GetCashierFromPayment(
            JsonElement payment)
        {
            string referenceNumber =
                payment.TryGetProperty(
                    "PaymentRefNum",
                    out JsonElement referenceElement)
                    ? referenceElement.GetString() ?? ""
                    : "";

            string cashierFromReference =
                GetCashierFromPaymentReference(
                    referenceNumber
                );

            if (!string.IsNullOrWhiteSpace(
                cashierFromReference))
            {
                return cashierFromReference;
            }

            /*
             * Como respaldo intentamos leer un campo
             * personalizado llamado CAJERO si QuickBooks
             * llegara a devolverlo en el pago.
             */
            return GetCashierFromTransactionJson(
                payment
            );
        }

        private static string GetCashierFromPaymentReference(
            string referenceNumber)
        {
            if (string.IsNullOrWhiteSpace(
                referenceNumber))
            {
                return "";
            }

            string clean =
                referenceNumber.Trim();

            const string prefix =
                "CAJERO:";

            int prefixPosition =
                clean.IndexOf(
                    prefix,
                    StringComparison.OrdinalIgnoreCase
                );

            if (prefixPosition < 0)
            {
                return "";
            }

            string cashierName =
                clean.Substring(
                    prefixPosition +
                    prefix.Length
                )
                .Trim();

            if (string.IsNullOrWhiteSpace(
                cashierName))
            {
                return "";
            }

            return NormalizeCashierName(
                cashierName
            );
        }

        private static string NormalizeCashierName(
            string cashierName)
        {
            string clean =
                (cashierName ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (clean == "ROCIO" ||
                clean == "ROCÍO" ||
                clean == "ROCIO RAMOS" ||
                clean == "ROCÍO RAMOS")
            {
                return "ROCIO RAMOS";
            }

            if (clean == "ADAN" ||
                clean == "ADÁN" ||
                clean == "ADAN HERNANDEZ" ||
                clean == "ADÁN HERNÁNDEZ")
            {
                return "ADAN HERNANDEZ";
            }

            if (clean == "FERNANDO" ||
                clean == "FERNANDO GOMEZ" ||
                clean == "FERNANDO GÓMEZ")
            {
                return "FERNANDO GOMEZ";
            }

            if (clean == "CARLOS" ||
                clean == "CARLOS LORENZANA")
            {
                return "CARLOS LORENZANA";
            }

            if (clean == "PAOLA" ||
                clean == "PAOLA VALLADARES")
            {
                return "PAOLA VALLADARES";
            }

            return clean;
        }

        public async Task<string> GetPayments(
    string? fechaDesde = null,
    string? fechaHasta = null)
        {
            string cacheKey =
                BuildCacheKey(
                    "qb-payments",
                    fechaDesde,
                    fechaHasta
                );

            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out string? cachedJson) &&
                !string.IsNullOrWhiteSpace(cachedJson))
            {
                return cachedJson;
            }

            var connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No hay conexión con QuickBooks."
                );
            }

            if (connection.AccessTokenExpiresAt <=
                DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshToken();
            }

            connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No se pudo recuperar la conexión con QuickBooks."
                );
            }

            var client =
                _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    connection.AccessToken
                );

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"
                )
            );

            string whereClause =
                BuildDateWhereClause(
                    fechaDesde,
                    fechaHasta
                );

            string queryText =
                $"SELECT * FROM Payment{whereClause} MAXRESULTS 1000";

            string query =
                Uri.EscapeDataString(queryText);

            string url =
                $"https://quickbooks.api.intuit.com/v3/company/" +
                $"{connection.RealmId}/query" +
                $"?query={query}" +
                $"&include=enhancedAllCustomFields";

            HttpResponseMessage response =
                await client.GetAsync(url);

            string responseText =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    "QuickBooks no pudo cargar los abonos.\n" +
                    $"Código HTTP: {(int)response.StatusCode} " +
                    $"{response.StatusCode}\n" +
                    responseText
                );
            }

            _memoryCache.Set(
                cacheKey,
                responseText,
                ShortCache()
            );

            return responseText;
        }

        public async Task<List<CreditPaymentDto>>
    GetCreditPaymentsListAsync(
        string? fechaDesde = null,
        string? fechaHasta = null)
        {
            string json =
                await GetPayments(
                    fechaDesde,
                    fechaHasta
                );

            var result =
                new List<CreditPaymentDto>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            using JsonDocument doc =
                JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(
                    "QueryResponse",
                    out JsonElement queryResponse))
            {
                return result;
            }

            if (!queryResponse.TryGetProperty(
                    "Payment",
                    out JsonElement payments) ||
                payments.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement payment in payments.EnumerateArray())
            {
                var dto =
                    new CreditPaymentDto
                    {
                        PaymentId =
                            payment.TryGetProperty(
                                "Id",
                                out JsonElement idElement)
                                ? idElement.GetString() ?? ""
                                : "",

                        PaymentDate =
                            payment.TryGetProperty(
                                "TxnDate",
                                out JsonElement dateElement) &&
                            DateTime.TryParse(
                                dateElement.GetString(),
                                out DateTime paymentDate)
                                ? paymentDate
                                : DateTime.Today,

                        CustomerName =
                            payment.TryGetProperty(
                                "CustomerRef",
                                out JsonElement customerRef) &&
                            customerRef.TryGetProperty(
                                "name",
                                out JsonElement customerNameElement)
                                ? customerNameElement.GetString() ?? ""
                                : "",

                        ReferenceNumber =
                            payment.TryGetProperty(
                                "PaymentRefNum",
                                out JsonElement referenceElement)
                                ? referenceElement.GetString() ?? ""
                                : "",

                        CashierName =
                            GetCashierFromPayment(
                                payment
                            ),

                        TotalAmount =
                            payment.TryGetProperty(
                                "TotalAmt",
                                out JsonElement totalElement) &&
                            totalElement.TryGetDecimal(
                                out decimal total)
                                ? total
                                : 0m
                    };

                if (payment.TryGetProperty(
                        "PaymentMethodRef",
                        out JsonElement paymentMethodRef) &&
                    paymentMethodRef.TryGetProperty(
                        "name",
                        out JsonElement paymentMethodName))
                {
                    dto.PaymentMethod =
                        paymentMethodName.GetString() ?? "";
                }

                if (payment.TryGetProperty(
                        "Line",
                        out JsonElement lines) &&
                    lines.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement line in lines.EnumerateArray())
                    {
                        if (!line.TryGetProperty(
                                "LinkedTxn",
                                out JsonElement linkedTransactions) ||
                            linkedTransactions.ValueKind !=
                                JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (
                            JsonElement linkedTransaction
                            in linkedTransactions.EnumerateArray())
                        {
                            string transactionType =
                                linkedTransaction.TryGetProperty(
                                    "TxnType",
                                    out JsonElement typeElement)
                                    ? typeElement.GetString() ?? ""
                                    : "";

                            if (!transactionType.Equals(
                                    "Invoice",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string invoiceId =
                                linkedTransaction.TryGetProperty(
                                    "TxnId",
                                    out JsonElement invoiceIdElement)
                                    ? invoiceIdElement.GetString() ?? ""
                                    : "";

                            if (!string.IsNullOrWhiteSpace(invoiceId))
                            {
                                dto.InvoiceIds.Add(invoiceId);
                            }
                        }
                    }
                }

                /*
                 * Solo consideramos abonos que estén vinculados
                 * a una factura de crédito.
                 */
                if (dto.InvoiceIds.Count > 0)
                {
                    result.Add(dto);
                }
            }

            return result
                .OrderByDescending(x => x.PaymentDate)
                .ToList();
        }

        public async Task<string> GetItemsAsync()
        {
            const string cacheKey =
                "qb-items-catalog";

            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out string? cachedJson) &&
                !string.IsNullOrWhiteSpace(cachedJson))
            {
                return cachedJson;
            }

            QuickBooksConnection? connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No hay conexión con QuickBooks."
                );
            }

            if (connection.AccessTokenExpiresAt <=
                DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshToken();
            }

            connection =
                await _db.QuickBooksConnections
                    .FirstOrDefaultAsync();

            if (connection == null)
            {
                throw new Exception(
                    "No se pudo recuperar la conexión con QuickBooks."
                );
            }

            var client =
                _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    connection.AccessToken
                );

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"
                )
            );

            var allItems =
                new List<JsonElement>();

            int startPosition = 1;
            const int maxResults = 1000;

            while (true)
            {
                string queryText =
                    "SELECT * FROM Item " +
                    $"STARTPOSITION {startPosition} " +
                    $"MAXRESULTS {maxResults}";

                string query =
                    Uri.EscapeDataString(queryText);

                string url =
                    $"https://quickbooks.api.intuit.com/v3/company/" +
                    $"{connection.RealmId}/query?query={query}";

                HttpResponseMessage response =
                    await client.GetAsync(url);

                string responseText =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        "QuickBooks no pudo cargar los productos.\n" +
                        $"Código HTTP: {(int)response.StatusCode} " +
                        $"{response.StatusCode}\n" +
                        responseText
                    );
                }

                using JsonDocument pageDocument =
                    JsonDocument.Parse(responseText);

                if (!pageDocument.RootElement.TryGetProperty(
                        "QueryResponse",
                        out JsonElement queryResponse))
                {
                    break;
                }

                if (!queryResponse.TryGetProperty(
                        "Item",
                        out JsonElement items) ||
                    items.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                int pageCount =
                    items.GetArrayLength();

                foreach (JsonElement item in items.EnumerateArray())
                {
                    allItems.Add(item.Clone());
                }

                if (pageCount < maxResults)
                {
                    break;
                }

                startPosition += maxResults;
            }

            string resultJson =
                JsonSerializer.Serialize(new
                {
                    QueryResponse = new
                    {
                        Item = allItems
                    }
                });

            _memoryCache.Set(
                cacheKey,
                resultJson,
                MediumCache()
            );

            return resultJson;
        }

        public async Task<List<QuickBooksItemReportDto>>
            GetItemsForReportsAsync()
        {
            string json =
                await GetItemsAsync();

            var result =
                new List<QuickBooksItemReportDto>();

            using JsonDocument document =
                JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty(
                    "QueryResponse",
                    out JsonElement queryResponse))
            {
                return result;
            }

            if (!queryResponse.TryGetProperty(
                    "Item",
                    out JsonElement items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                bool active =
                    !item.TryGetProperty(
                        "Active",
                        out JsonElement activeElement) ||
                    activeElement.ValueKind == JsonValueKind.True;

                if (!active)
                {
                    continue;
                }

                string itemId =
                    item.TryGetProperty(
                        "Id",
                        out JsonElement idElement)
                        ? idElement.GetString() ?? ""
                        : "";

                string name =
                    item.TryGetProperty(
                        "Name",
                        out JsonElement nameElement)
                        ? nameElement.GetString() ?? ""
                        : "";

                string sku =
                    item.TryGetProperty(
                        "Sku",
                        out JsonElement skuElement)
                        ? skuElement.GetString() ?? ""
                        : "";

                decimal purchaseCost = 0m;

                if (item.TryGetProperty(
                        "PurchaseCost",
                        out JsonElement costElement))
                {
                    costElement.TryGetDecimal(
                        out purchaseCost
                    );
                }

                decimal unitPrice = 0m;

                if (item.TryGetProperty(
                        "UnitPrice",
                        out JsonElement priceElement))
                {
                    priceElement.TryGetDecimal(
                        out unitPrice
                    );
                }

                result.Add(
                    new QuickBooksItemReportDto
                    {
                        ItemId = itemId,

                        Name = string.IsNullOrWhiteSpace(name)
                            ? "Producto"
                            : name,

                        /*
                         * Temporalmente usamos SKU como marca.
                         * Si el SKU está vacío aparecerá "Sin marca".
                         */
                        Brand = string.IsNullOrWhiteSpace(sku)
                            ? "Sin marca"
                            : sku,

                        PurchaseCost = purchaseCost,

                        UnitPrice = unitPrice
                    }
                );
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