using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.Models;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

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

        private static async Task<HttpResponseMessage>
    GetQuickBooksWithRetryAsync(
        HttpClient client,
        string url)
        {
            Exception? lastError = null;

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var timeout =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(30)
                        );

                    HttpResponseMessage response =
                        await client.GetAsync(
                            url,
                            HttpCompletionOption.ResponseHeadersRead,
                            timeout.Token
                        );

                    return response;
                }
                catch (OperationCanceledException ex)
                {
                    lastError = ex;

                    Console.WriteLine(
                        $"QuickBooks timeout. Intento {attempt}/2."
                    );
                }
                catch (HttpRequestException ex)
                {
                    lastError = ex;

                    Console.WriteLine(
                        $"QuickBooks error HTTP. Intento {attempt}/2: " +
                        ex.Message
                    );
                }

                if (attempt < 2)
                {
                    await Task.Delay(750);
                }
            }

            throw new Exception(
                "QuickBooks no respondió después de dos intentos. " +
                "Revise la conexión entre el servidor y QuickBooks.",
                lastError
            );
        }

        // fechaDesde / fechaHasta en formato "yyyy-MM-dd". Si vienen null/vacíos, no se filtra por fecha.
        public async Task<string> GetSalesReceipts(string? fechaDesde = null, string? fechaHasta = null)
        {
            /*
             * No usar caché aquí.
             * Los dashboards dependen de esta consulta para detectar
             * ventas nuevas inmediatamente al presionar Actualizar.
             */

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
                await GetQuickBooksWithRetryAsync(
    client,
    url
);

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

        public async Task<(DashboardSyncResponse Result, string DocumentJson)>
            SynchronizeDashboardDocumentAsync(
                string quickBooksId,
                string priceType,
                decimal creditPercentage,
                IReadOnlyCollection<ItemDiscountRequest>? discounts)
        {
            quickBooksId = (quickBooksId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(quickBooksId))
                throw new Exception("El ID de QuickBooks es obligatorio.");

            priceType = NormalizeDashboardPriceType(priceType);
            creditPercentage = priceType == "credito" ? 3m : 0m;
            discounts ??= Array.Empty<ItemDiscountRequest>();

            Invoice? stored = await _db.Invoices
                .Include(x => x.Lines)
                .Where(x => x.QuickBooksId == quickBooksId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (stored != null && stored.IsCancelled)
                throw new Exception("La factura está anulada.");

            /*
             * No confiamos únicamente en el total guardado localmente.
             * Siempre consultamos y, cuando corresponda, actualizamos
             * QuickBooks. Esto evita que el Dashboard muestre un total
             * distinto al que realmente tiene la factura en QuickBooks.
             */

            if (stored != null && stored.IsCertified)
            {
                bool samePrice =
                    string.Equals(
                        stored.PriceType,
                        priceType,
                        StringComparison.OrdinalIgnoreCase
                    );

                decimal requestedDiscount =
                    Math.Round(
                        discounts.Sum(
                            x => x?.Amount ?? 0m
                        ),
                        2,
                        MidpointRounding.AwayFromZero
                    );

                bool sameDiscount =
                    Math.Abs(
                        stored.DiscountTotal -
                        requestedDiscount
                    ) <= 0.009m;

                if (!samePrice || !sameDiscount)
                {
                    throw new Exception(
                        "Una factura certificada no puede cambiar precios ni descuentos."
                    );
                }

                string existingJson =
                    await GetDashboardDocumentJsonAsync(
                        quickBooksId
                    );

                return (
                    new DashboardSyncResponse
                    {
                        Success = true,
                        Message =
                            "La factura está certificada. Se conservaron sus valores.",
                        QuickBooksId =
                            quickBooksId,
                        Subtotal =
                            stored.Subtotal,
                        DiscountTotal =
                            stored.DiscountTotal,
                        Total =
                            stored.Total,
                        PriceType =
                            stored.PriceType,
                        WasAlreadySynchronized =
                            true
                    },
                    existingJson
                );
            }

            string currentJson = await GetDashboardDocumentJsonAsync(quickBooksId);
            var parsed = ParseQueryDocument(currentJson);
            JsonObject qbDocument = parsed.Document;
            string entityName = parsed.EntityName;

            var discountMap = discounts
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.LineId))
                .GroupBy(x => x.LineId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Amount), 2), StringComparer.OrdinalIgnoreCase);

            var oldLines = stored?.Lines?.Where(x => !string.IsNullOrWhiteSpace(x.QuickBooksLineId))
                .ToDictionary(x => x.QuickBooksLineId, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, InvoiceLine>(StringComparer.OrdinalIgnoreCase);

            var savedLines = new List<InvoiceLine>();
            decimal subtotal = 0m, discountTotal = 0m, finalTotal = 0m;
            JsonArray lines = qbDocument["Line"] as JsonArray ?? new JsonArray();

            foreach (JsonNode? node in lines)
            {
                if (node is not JsonObject line) continue;
                if (!string.Equals(line["DetailType"]?.GetValue<string>(), "SalesItemLineDetail", StringComparison.OrdinalIgnoreCase)) continue;
                if (line["SalesItemLineDetail"] is not JsonObject detail) continue;

                string lineId = line["Id"]?.ToString() ?? "";
                decimal qty = ReadDecimal(detail["Qty"], 1m);
                if (qty <= 0m) qty = 1m;
                decimal currentUnit = ReadDecimal(detail["UnitPrice"], 0m);
                decimal originalUnit = oldLines.TryGetValue(lineId, out InvoiceLine? old) && old.OriginalUnitPrice > 0m
                    ? old.OriginalUnitPrice : currentUnit;
                decimal appliedUnit = priceType == "credito"
                    ? Math.Round(originalUnit * 1.03m, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(originalUnit, 2, MidpointRounding.AwayFromZero);
                decimal lineSubtotal = Math.Round(appliedUnit * qty, 2, MidpointRounding.AwayFromZero);
                decimal lineDiscount = discountMap.TryGetValue(lineId, out decimal d) ? d : 0m;
                if (lineDiscount < 0m || lineDiscount > lineSubtotal)
                    throw new Exception($"El descuento de la línea {lineId} no es válido.");
                decimal lineTotal = lineSubtotal - lineDiscount;

                detail["UnitPrice"] = appliedUnit;
                detail["Qty"] = qty;
                detail["DiscountAmt"] = lineDiscount;
                line["Amount"] = lineSubtotal;

                string itemId = detail["ItemRef"]?["value"]?.ToString() ?? "";
                string description = detail["ItemRef"]?["name"]?.ToString() ?? line["Description"]?.ToString() ?? "Producto";
                savedLines.Add(new InvoiceLine
                {
                    QuickBooksLineId = lineId,
                    QuickBooksItemId = itemId,
                    Description = description,
                    Quantity = qty,
                    OriginalUnitPrice = originalUnit,
                    AppliedUnitPrice = appliedUnit,
                    OriginalSubtotal = Math.Round(originalUnit * qty, 2, MidpointRounding.AwayFromZero),
                    DiscountAmount = lineDiscount,
                    FinalTotal = lineTotal,
                    CreatedAt = DateTime.UtcNow
                });
                subtotal += lineSubtotal; discountTotal += lineDiscount; finalTotal += lineTotal;
            }

            if (savedLines.Count == 0) throw new Exception("El documento no contiene productos para actualizar.");

            JsonObject updatePayload = new JsonObject
            {
                ["Id"] = qbDocument["Id"]?.DeepClone(),
                ["SyncToken"] = qbDocument["SyncToken"]?.DeepClone(),
                ["sparse"] = true,
                ["Line"] = lines.DeepClone()
            };

            string updatedJson = await PostQuickBooksUpdateAsync(entityName, updatePayload);
            string wrappedJson = WrapUpdatedDocument(updatedJson, entityName);

            if (stored == null)
            {
                stored = new Invoice { QuickBooksId = quickBooksId, CreatedAt = DateTime.UtcNow };
                _db.Invoices.Add(stored);
            }
            else if (stored.Lines.Count > 0)
            {
                _db.InvoiceLines.RemoveRange(stored.Lines);
            }

            stored.InvoiceNumber = qbDocument["DocNumber"]?.ToString() ?? quickBooksId;
            stored.CustomerName = qbDocument["CustomerRef"]?["name"]?.ToString() ?? "Consumidor Final";
            stored.CustomerNit = string.IsNullOrWhiteSpace(stored.CustomerNit) ? "CF" : stored.CustomerNit;
            if (DateTime.TryParse(
                    qbDocument["TxnDate"]?.ToString(),
                    out DateTime issue))
            {
                stored.IssueDate =
                    DateTime.SpecifyKind(
                        issue,
                        DateTimeKind.Utc
                    );
            }
            else
            {
                stored.IssueDate =
                    DateTime.UtcNow;
            }
            stored.Subtotal = subtotal; stored.DiscountTotal = discountTotal; stored.Total = finalTotal;
            stored.SaleType = entityName == "Invoice" ? "credito" : "contado";
            stored.PriceType = priceType; stored.CreditPercentage = creditPercentage;
            stored.Status = "dashboard-synced"; stored.Lines = savedLines;
            await _db.SaveChangesAsync();

            _memoryCache.Remove("qb-sales-receipt|" + quickBooksId);
            _memoryCache.Remove("qb-invoice|" + quickBooksId);

            return (new DashboardSyncResponse
            {
                Success = true,
                Message = "Precios y descuentos actualizados en QuickBooks.",
                QuickBooksId = quickBooksId,
                Subtotal = subtotal,
                DiscountTotal = discountTotal,
                Total = finalTotal,
                PriceType = priceType,
                WasAlreadySynchronized = false
            }, wrappedJson);
        }

        public async Task<List<DashboardSyncResponse>> SynchronizeHistoricalDiscountsAsync()
        {
            List<Invoice> invoices = await _db.Invoices.Include(x => x.Lines)
                .Where(x => !x.IsCancelled && x.DiscountTotal > 0m)
                .OrderBy(x => x.IssueDate).ToListAsync();
            var results = new List<DashboardSyncResponse>();
            foreach (Invoice invoice in invoices)
            {
                var discounts = invoice.Lines.Where(x => x.DiscountAmount > 0m && !string.IsNullOrWhiteSpace(x.QuickBooksLineId))
                    .Select(x => new ItemDiscountRequest { LineId = x.QuickBooksLineId, Amount = x.DiscountAmount }).ToList();
                if (discounts.Count == 0) continue;
                try
                {
                    var sync = await SynchronizeDashboardDocumentAsync(invoice.QuickBooksId, invoice.PriceType, invoice.CreditPercentage, discounts);
                    results.Add(sync.Result);
                }
                catch (Exception ex)
                {
                    results.Add(new DashboardSyncResponse { Success = false, QuickBooksId = invoice.QuickBooksId, Message = ex.Message });
                }
            }
            return results;
        }

        private async Task<string> GetDashboardDocumentJsonAsync(
            string id)
        {
            string salesReceiptJson =
                await GetSalesReceiptById(id);

            if (ContainsQuickBooksEntity(
                salesReceiptJson,
                "SalesReceipt"))
            {
                return salesReceiptJson;
            }

            string invoiceJson =
                await GetInvoiceById(id);

            if (ContainsQuickBooksEntity(
                invoiceJson,
                "Invoice"))
            {
                return invoiceJson;
            }

            throw new Exception(
                "No se encontró la venta en QuickBooks con el ID " +
                id +
                "."
            );
        }

        private static bool ContainsQuickBooksEntity(
            string json,
            string entityName)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                JsonNode? root =
                    JsonNode.Parse(json);

                JsonArray? documents =
                    root?["QueryResponse"]?[entityName]
                    as JsonArray;

                return documents != null &&
                    documents.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static (JsonObject Document, string EntityName) ParseQueryDocument(string json)
        {
            JsonNode root = JsonNode.Parse(json) ?? throw new Exception("QuickBooks devolvió JSON vacío.");
            JsonObject query = root["QueryResponse"] as JsonObject ?? throw new Exception("Respuesta de QuickBooks inválida.");
            foreach (string name in new[] { "SalesReceipt", "Invoice" })
            {
                if (query[name] is JsonArray array && array.Count > 0 && array[0] is JsonObject obj) return (obj, name);
            }
            throw new Exception("QuickBooks no devolvió SalesReceipt ni Invoice.");
        }

        private async Task<string> PostQuickBooksUpdateAsync(string entityName, JsonObject payload)
        {
            var connection = _db.QuickBooksConnections.FirstOrDefault() ?? throw new Exception("No hay conexión con QuickBooks.");
            if (connection.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5)) await RefreshToken();
            connection = _db.QuickBooksConnections.First();
            HttpClient client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            string entity = entityName.Equals("Invoice", StringComparison.OrdinalIgnoreCase) ? "invoice" : "salesreceipt";
            string url = $"https://quickbooks.api.intuit.com/v3/company/{connection.RealmId}/{entity}?operation=update&minorversion=75";
            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(url, content);
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception("QuickBooks no pudo actualizar la venta.\n" + text);
            return text;
        }

        private static string WrapUpdatedDocument(string json, string entityName)
        {
            JsonNode root = JsonNode.Parse(json) ?? throw new Exception("Respuesta de actualización vacía.");
            JsonNode? entity = root[entityName];
            if (entity == null) return json;
            var wrapper = new JsonObject { ["QueryResponse"] = new JsonObject { [entityName] = new JsonArray(entity.DeepClone()) } };
            return wrapper.ToJsonString();
        }

        public async Task<string> CancelDocumentInQuickBooksAsync(
            string quickBooksId)
        {
            quickBooksId =
                (quickBooksId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(
                quickBooksId))
            {
                throw new Exception(
                    "El ID de QuickBooks es obligatorio para anular la venta."
                );
            }

            /*
             * Eliminamos cualquier copia en caché antes de consultar.
             * Esto evita usar un SyncToken anterior.
             */
            _memoryCache.Remove(
                "qb-sales-receipt|" + quickBooksId
            );

            _memoryCache.Remove(
                "qb-invoice|" + quickBooksId
            );

            string json =
                await GetSalesReceiptById(
                    quickBooksId
                );

            string entityName =
                "SalesReceipt";

            if (!json.Contains(
                    "\"SalesReceipt\"",
                    StringComparison.Ordinal))
            {
                json =
                    await GetInvoiceById(
                        quickBooksId
                    );

                entityName =
                    "Invoice";
            }

            if (!json.Contains(
                    "\"" + entityName + "\"",
                    StringComparison.Ordinal))
            {
                /*
                 * Si ya no existe, consideramos que QuickBooks
                 * ya quedó corregido. Esto permite reintentar una
                 * anulación sin provocar otro error.
                 */
                InvalidateAllCachesAfterCancellation(
                    quickBooksId
                );

                return
                    "La venta ya no existe en QuickBooks.";
            }

            (JsonObject document, string parsedEntity) =
                ParseQueryDocument(
                    json
                );

            entityName =
                parsedEntity;

            string id =
                document["Id"]?.ToString()
                ?? quickBooksId;

            string syncToken =
                document["SyncToken"]?.ToString()
                ?? "";

            if (string.IsNullOrWhiteSpace(
                syncToken))
            {
                throw new Exception(
                    "QuickBooks no devolvió el SyncToken necesario para anular la venta."
                );
            }

            QuickBooksConnection connection =
                _db.QuickBooksConnections
                    .FirstOrDefault()
                ?? throw new Exception(
                    "No hay conexión con QuickBooks."
                );

            if (connection.AccessTokenExpiresAt <=
                DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshToken();

                connection =
                    _db.QuickBooksConnections
                        .First();
            }

            HttpClient client =
                _httpClientFactory.CreateClient();

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

            string entityPath =
                entityName.Equals(
                    "Invoice",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "invoice"
                    : "salesreceipt";

            string url =
                $"https://quickbooks.api.intuit.com/v3/company/{connection.RealmId}/{entityPath}?operation=delete&minorversion=75";

            JsonObject payload =
                new JsonObject
                {
                    ["Id"] = id,
                    ["SyncToken"] = syncToken
                };

            using (
                var content =
                    new StringContent(
                        payload.ToJsonString(),
                        Encoding.UTF8,
                        "application/json"
                    )
            )
            {
                HttpResponseMessage response =
                    await client.PostAsync(
                        url,
                        content
                    );

                string responseText =
                    await response.Content
                        .ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        "QuickBooks no pudo anular la venta.\n" +
                        responseText
                    );
                }
            }

            InvalidateAllCachesAfterCancellation(
                quickBooksId
            );

            return
                "La venta fue eliminada de QuickBooks correctamente.";
        }

        private void InvalidateAllCachesAfterCancellation(
            string quickBooksId)
        {
            _memoryCache.Remove(
                "qb-sales-receipt|" + quickBooksId
            );

            _memoryCache.Remove(
                "qb-invoice|" + quickBooksId
            );

            /*
             * Los reportes usan claves por rango de fechas.
             * Compactar la caché garantiza que el corte, ventas,
             * productos y demás reportes se recalculen inmediatamente.
             */
            if (_memoryCache is MemoryCache memoryCache)
            {
                memoryCache.Compact(
                    1.0
                );
            }
        }

        private static string NormalizeDashboardPriceType(string? value)
        {
            string result = (value ?? "contado").Trim().ToLowerInvariant().Replace("é", "e").Replace("í", "i");
            if (result != "contado" && result != "credito") throw new Exception("El tipo de precio debe ser contado o crédito.");
            return result;
        }

        private static decimal ReadDecimal(JsonNode? node, decimal fallback)
        {
            if (node == null) return fallback;
            return decimal.TryParse(node.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value) ? value : fallback;
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

            var response =
    await GetQuickBooksWithRetryAsync(
        client,
        url
    );
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
            /*
             * No usar caché aquí.
             * El dashboard de crédito debe detectar facturas nuevas
             * inmediatamente al presionar Actualizar.
             */

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

            var response = await GetQuickBooksWithRetryAsync(
    client,
    url
);

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

                // Solo las facturas identificadas con un cajero llegan al Dashboard.
                // Las facturas administrativas (por ejemplo, las creadas por Celeste)
                // quedan en QuickBooks, pero no aparecen en Clientes / Crédito.
                string privateNote =
                    inv.TryGetProperty("PrivateNote", out var privateNoteElement)
                        ? privateNoteElement.GetString() ?? ""
                        : "";

                bool administrativeInvoice =
                    string.IsNullOrWhiteSpace(cashierName) ||
                    cashierName.Contains("CELESTE", StringComparison.OrdinalIgnoreCase) ||
                    privateNote.Contains("NO CAJA", StringComparison.OrdinalIgnoreCase);

                if (administrativeInvoice)
                {
                    continue;
                }

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

            var response = await GetQuickBooksWithRetryAsync(
    client,
    url
);
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
                await GetQuickBooksWithRetryAsync(
    client,
    url
);

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
                    await GetQuickBooksWithRetryAsync(
    client,
    url
);

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
                await GetQuickBooksWithRetryAsync(
    client,
    url
);

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
                    await GetQuickBooksWithRetryAsync(
    client,
    url
);

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