using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using QBTicketsApi.DTOs;
using QBTicketsApi.DTOs.QBTicketsApi.DTOs;
using QBTicketsApi.Models;
using System.Text.Json;

namespace QBTicketsApi.Services
{
    public class ReportsService
    {
        private readonly AppDbContext _db;
        private readonly QuickBooksService _quickBooksService;
        private readonly FelService _felService;
        private readonly CashMovementService _cashMovementService;

        public ReportsService(
            AppDbContext db,
            QuickBooksService quickBooksService,
            FelService felService,
            CashMovementService cashMovementService)
        {
            _db = db;
            _quickBooksService = quickBooksService;
            _felService = felService;
            _cashMovementService = cashMovementService;
        }

        public async Task<SalesReportResponseDto>
            GetSalesReportAsync(
                string? desde,
                string? hasta)
        {
            List<InvoiceResponseDto> cashSales =
                await _quickBooksService
                    .GetSalesReceiptsList(
                        desde,
                        hasta
                    );

            List<InvoiceResponseDto> creditSales =
                await _quickBooksService
                    .GetCreditInvoicesList(
                        desde,
                        hasta
                    );

            List<InvoiceResponseDto> allSales =
                cashSales
                    .Concat(creditSales)
                    .OrderByDescending(x => x.IssueDate)
                    .ToList();

            List<string> quickBooksIds =
                allSales
                    .Select(x => x.QbInvoiceId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();

            List<Invoice> storedInvoices =
                await _db.Invoices
                    .AsNoTracking()
                    .Where(x =>
                        quickBooksIds.Contains(x.QuickBooksId)
                    )
                    .ToListAsync();

            var storedByQuickBooksId =
                storedInvoices
                    .GroupBy(x => x.QuickBooksId)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderByDescending(x => x.CreatedAt)
                            .First()
                    );

            var response =
                new SalesReportResponseDto();

            foreach (InvoiceResponseDto sale in allSales)
            {
                storedByQuickBooksId.TryGetValue(
                    sale.QbInvoiceId,
                    out Invoice? stored
                );

                bool isCancelled =
                    stored != null &&
                    stored.IsCancelled;

                bool isCertified =
                    stored != null &&
                    stored.IsCertified &&
                    !string.IsNullOrWhiteSpace(
                        stored.FelAuthorizationNumber
                    );

                string paymentMethod =
                    sale.SaleType.Equals(
                        "credito",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Crédito"
                        : await GetPaymentMethodAsync(
                            sale.QbInvoiceId,
                            sale.SaleType
                        );

                response.Sales.Add(
                    new SalesReportRowDto
                    {
                        QuickBooksId =
                            sale.QbInvoiceId,

                        IssueDate =
                            sale.IssueDate,

                        SaleType =
                            sale.SaleType,

                        PaymentMethod =
                            paymentMethod,

                        CustomerName =
                            sale.CustomerName,

                        CustomerNit =
                            sale.CustomerNit,

                        InvoiceNumber =
                            sale.InvoiceNumber,

                        Correlative =
                            sale.InvoiceNumber,

                        AuthorizationNumber =
                            isCertified
                                ? stored!.FelAuthorizationNumber
                                : "PENDIENTE",

                        Serie =
                            isCertified
                                ? stored!.FelSerie
                                : "PENDIENTE",

                        DteNumber =
                            isCertified &&
                            !string.IsNullOrWhiteSpace(
                                stored!.FelDteNumber
                            )
                                ? stored.FelDteNumber
                                : sale.InvoiceNumber,

                        Status =
                            isCancelled
                                ? "ANULADA"
                                : isCertified
                                    ? "CERTIFICADA"
                                    : "ENVIO",

                        Total =
                            stored != null &&
                            stored.Total > 0
                                ? stored.Total
                                : sale.Total,

                        CancellationReason =
                            stored?.CancellationReason
                            ?? "",

                        CashierName =
                            sale.CashierName,

                        IsCertified =
                            isCertified,

                        CanRetryCertification =
                            !isCertified &&
                            !isCancelled
                    }
                );
            }

            response.Total =
                response.Sales.Sum(x => x.Total);

            return response;
        }

        public async Task<SaleDetailDto>
            GetSaleDetailAsync(string quickBooksId)
        {
            if (string.IsNullOrWhiteSpace(quickBooksId))
            {
                throw new Exception(
                    "El ID de la venta es obligatorio."
                );
            }

            string json =
                await _quickBooksService
                    .GetSalesReceiptById(quickBooksId);

            string saleType = "contado";

            if (!ContainsDocument(
                    json,
                    "SalesReceipt"))
            {
                json =
                    await _quickBooksService
                        .GetInvoiceById(quickBooksId);

                saleType = "credito";
            }

            if (!ContainsDocument(
                    json,
                    saleType == "contado"
                        ? "SalesReceipt"
                        : "Invoice"))
            {
                throw new Exception(
                    "No se encontró la venta en QuickBooks."
                );
            }

            InvoiceItemsResponseDto itemResponse =
                await _quickBooksService
                    .GetDocumentItemsAsync(
                        quickBooksId
                    );

            Invoice? stored =
                await _db.Invoices
                    .AsNoTracking()
                    .Where(x =>
                        x.QuickBooksId == quickBooksId
                    )
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync();

            JsonElement qbDocument =
                GetQuickBooksDocument(
                    json,
                    saleType == "contado"
                        ? "SalesReceipt"
                        : "Invoice"
                );

            string customerName =
                GetReferenceName(
                    qbDocument,
                    "CustomerRef",
                    "Consumidor Final"
                );

            string customerNit =
                string.IsNullOrWhiteSpace(
                    stored?.CustomerNit)
                    ? "CF"
                    : stored.CustomerNit;

            if (customerNit.Equals(
                "CF",
                StringComparison.OrdinalIgnoreCase))
            {
                customerName = "Consumidor Final";
            }
            else if (!string.IsNullOrWhiteSpace(
                stored?.CustomerName))
            {
                customerName =
                    stored.CustomerName;
            }

            DateTime issueDate =
                GetDate(
                    qbDocument,
                    "TxnDate"
                );

            string invoiceNumber =
                GetString(
                    qbDocument,
                    "DocNumber"
                );

            string paymentMethod =
                saleType == "credito"
                    ? "Crédito"
                    : GetPaymentMethod(
                        qbDocument
                    );

            string address =
                GetAddress(qbDocument);

            string voucher =
                GetVoucher(qbDocument);

            string cashierName =
                await _quickBooksService
                    .GetDocumentCashierNameAsync(
                        quickBooksId
                    );

            bool isCancelled =
                stored != null &&
                stored.IsCancelled;

            bool isCertified =
                stored != null &&
                stored.IsCertified &&
                !string.IsNullOrWhiteSpace(
                    stored.FelAuthorizationNumber
                );

            var detail =
                new SaleDetailDto
                {
                    QuickBooksId =
                        quickBooksId,

                    CustomerName =
                        customerName,

                    InvoiceName =
                        customerName,

                    CustomerNit =
                        customerNit,

                    Correlative =
                        invoiceNumber,

                    DocumentNumber =
                        !string.IsNullOrWhiteSpace(
                            stored?.FelDteNumber)
                            ? stored.FelDteNumber
                            : invoiceNumber,

                    DteType =
                        "FACT",

                    Voucher =
                        voucher,

                    PaymentMethod =
                        paymentMethod,

                    Address =
                        address,

                    IssueDate =
                        issueDate,

                    AuthorizationNumber =
                        stored?.FelAuthorizationNumber
                        ?? "",

                    Serie =
                        stored?.FelSerie
                        ?? "",

                    DteNumber =
                        stored?.FelDteNumber
                        ?? "",

                    Status =
                        isCancelled
                            ? "ANULADA"
                            : isCertified
                                ? "CERTIFICADA"
                                : "ENVIO",

                    Qr =
                        stored?.FelQr
                        ?? "",

                    CashierName =
                        cashierName,

                    SaleType =
                        saleType,

                    Subtotal =
                        itemResponse.Subtotal,

                    DiscountTotal =
                        itemResponse.DiscountTotal,

                    Total =
                        stored != null &&
                        stored.Total > 0
                            ? stored.Total
                            : itemResponse.Total,

                    IsCertified =
                        isCertified,

                    CanRetryCertification =
                        !isCertified &&
                        !isCancelled
                };

            detail.Items =
                itemResponse.Items
                    .Select(item =>
                        new SaleDetailItemDto
                        {
                            LineId =
                                item.LineId,

                            ItemId =
                                item.ItemId,

                            Quantity =
                                item.Quantity,

                            Description =
                                item.Description,

                            UnitPrice =
                                item.UnitPrice,

                            Discount =
                                item.CurrentDiscount,

                            Total =
                                item.Total
                        }
                    )
                    .ToList();

            return detail;
        }

        public async Task<RetryCertificationResponseDto> RetryCertificationAsync(string quickBooksId, RetryCertificationRequestDto request)
        {
            Invoice? existing =
                await _db.Invoices
                    .AsNoTracking()
                    .Where(x =>
                        x.QuickBooksId == quickBooksId &&
                        x.IsCertified
                    )
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync();

            if (existing != null &&
                !string.IsNullOrWhiteSpace(
                    existing.FelAuthorizationNumber))
            {
                return new RetryCertificationResponseDto
                {
                    Success = false,

                    Message =
                        "La factura ya está certificada.",

                    Serie =
                        existing.FelSerie,

                    DteNumber =
                        existing.FelDteNumber,

                    AuthorizationNumber =
                        existing.FelAuthorizationNumber,

                    CertificationDate =
                        existing.FelCertificationDate
                        ?? DateTime.UtcNow,

                    Qr =
                        existing.FelQr
                };
            }

            string json =
                await _quickBooksService
                    .GetSalesReceiptById(quickBooksId);

            string saleType = "contado";

            if (!ContainsDocument(
                    json,
                    "SalesReceipt"))
            {
                json =
                    await _quickBooksService
                        .GetInvoiceById(quickBooksId);

                saleType = "credito";
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new Exception(
                    "No se encontró la venta en QuickBooks."
                );
            }

            string nit =
                string.IsNullOrWhiteSpace(request.Nit)
                    ? "CF"
                    : request.Nit.Trim();

            string customerName =
                string.IsNullOrWhiteSpace(
                    request.CustomerName)
                    ? "Consumidor Final"
                    : request.CustomerName.Trim();

            FelResult fel =
                await _felService.CertifyAsync(
                    quickBooksId,
                    json,
                    saleType,
                    nit,
                    customerName,
                    Array.Empty<ItemDiscountRequest>()
                );

            return new RetryCertificationResponseDto
            {
                Success = true,

                Message =
                    "Factura certificada correctamente.",

                Serie =
                    fel.Serie,

                DteNumber =
                    fel.DteNumber,

                AuthorizationNumber =
                    fel.AuthorizationNumber,

                CertificationDate =
                    fel.CertificationDate,

                Qr =
                    fel.Qr
            };
        }

        public async Task<CashierCutDto> GetCashierCutAsync(
    string cashierName,
    DateTime date,
    decimal openingBalanceFromScreen)
        {
            if (string.IsNullOrWhiteSpace(cashierName))
            {
                throw new Exception(
                    "Debe seleccionar un cajero."
                );
            }

            string finalCashierName =
                cashierName.Trim();

            DateTime selectedDate =
                date.Date;

            string dateText =
                selectedDate.ToString("yyyy-MM-dd");

            /*
             * PostgreSQL usa timestamp with time zone,
             * por eso las fechas de la consulta deben ser UTC.
             */
            DateTime dateUtc =
                DateTime.SpecifyKind(
                    selectedDate,
                    DateTimeKind.Utc
                );

            DateTime nextDateUtc =
                dateUtc.AddDays(1);

            List<InvoiceResponseDto> cashReceipts =
                await _quickBooksService
                    .GetSalesReceiptsList(
                        dateText,
                        dateText
                    );

            cashReceipts =
                cashReceipts
                    .Where(x =>
                        string.Equals(
                            x.CashierName?.Trim(),
                            finalCashierName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();

            decimal cashSales = 0m;
            decimal checkSales = 0m;
            decimal creditCardSales = 0m;

            foreach (InvoiceResponseDto sale in cashReceipts)
            {
                string paymentMethod =
                    await GetPaymentMethodAsync(
                        sale.QbInvoiceId,
                        sale.SaleType
                    );

                if (paymentMethod.Equals(
                    "Efectivo",
                    StringComparison.OrdinalIgnoreCase))
                {
                    cashSales +=
                        sale.Total;
                }
                else if (paymentMethod.Equals(
                    "Cheque",
                    StringComparison.OrdinalIgnoreCase))
                {
                    checkSales +=
                        sale.Total;
                }
                else if (paymentMethod.Equals(
                    "Tarjeta de crédito",
                    StringComparison.OrdinalIgnoreCase))
                {
                    creditCardSales +=
                        sale.Total;
                }
            }

            decimal storedOpeningBalance =
                await _db.CashMovements
                    .AsNoTracking()
                    .Where(x =>
                        x.MovementType == "OPENING_BALANCE" &&
                        x.CashierName == finalCashierName &&
                        x.MovementDate >= dateUtc &&
                        x.MovementDate < nextDateUtc
                    )
                    .OrderByDescending(x =>
                        x.MovementDate
                    )
                    .Select(x =>
                        x.Amount
                    )
                    .FirstOrDefaultAsync();

            decimal openingBalance =
                openingBalanceFromScreen;

            if (openingBalance <= 0)
            {
                openingBalance =
                    storedOpeningBalance;
            }

            decimal totalExpenses =
                await _db.CashMovements
                    .AsNoTracking()
                    .Where(x =>
                        x.MovementType == "EXPENSE" &&
                        x.CashierName == finalCashierName &&
                        x.MovementDate >= dateUtc &&
                        x.MovementDate < nextDateUtc
                    )
                    .SumAsync(x =>
                        x.Amount
                    );

            List<CreditPaymentDto> payments =
                await _quickBooksService
                    .GetCreditPaymentsListAsync(
                        dateText,
                        dateText
                    );

            decimal creditPayments =
                0m;

            foreach (CreditPaymentDto payment in payments)
            {
                if (payment.InvoiceIds == null)
                {
                    continue;
                }

                foreach (string invoiceId in payment.InvoiceIds)
                {
                    string invoiceCashier =
                        await _quickBooksService
                            .GetDocumentCashierNameAsync(
                                invoiceId
                            );

                    if (string.Equals(
                        invoiceCashier?.Trim(),
                        finalCashierName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        creditPayments +=
                            payment.TotalAmount;

                        break;
                    }
                }
            }

            return new CashierCutDto
            {
                Date =
                    selectedDate,

                CashierName =
                    finalCashierName,

                OpeningBalance =
                    openingBalance,

                TotalExpenses =
                    totalExpenses,

                CashSales =
                    cashSales,

                CheckSales =
                    checkSales,

                CreditCardSales =
                    creditCardSales,

                CreditPayments =
                    creditPayments
            };
        }

        public async Task<GeneralCutDto> GetGeneralCutAsync(
    DateTime startDate,
    DateTime endDate)
        {
            DateTime finalStartDate =
                startDate.Date;

            DateTime finalEndDate =
                endDate.Date;

            if (finalEndDate < finalStartDate)
            {
                throw new Exception(
                    "La fecha final no puede ser menor que la fecha inicial."
                );
            }

            string startText =
                finalStartDate.ToString(
                    "yyyy-MM-dd"
                );

            string endText =
                finalEndDate.ToString(
                    "yyyy-MM-dd"
                );

            /*
             * PostgreSQL requiere DateTime UTC para columnas
             * timestamp with time zone.
             */
            DateTime startUtc =
                DateTime.SpecifyKind(
                    finalStartDate,
                    DateTimeKind.Utc
                );

            DateTime endUtc =
                DateTime.SpecifyKind(
                    finalEndDate.AddDays(1),
                    DateTimeKind.Utc
                );

            List<InvoiceResponseDto> cashReceipts =
                await _quickBooksService
                    .GetSalesReceiptsList(
                        startText,
                        endText
                    );

            decimal cashSales = 0;
            decimal checkSales = 0;
            decimal creditCardSales = 0;

            foreach (InvoiceResponseDto sale in cashReceipts)
            {
                string paymentMethod =
                    await GetPaymentMethodAsync(
                        sale.QbInvoiceId,
                        sale.SaleType
                    );

                if (paymentMethod.Equals(
                    "Efectivo",
                    StringComparison.OrdinalIgnoreCase))
                {
                    cashSales +=
                        sale.Total;
                }
                else if (paymentMethod.Equals(
                    "Cheque",
                    StringComparison.OrdinalIgnoreCase))
                {
                    checkSales +=
                        sale.Total;
                }
                else if (paymentMethod.Equals(
                    "Tarjeta de crédito",
                    StringComparison.OrdinalIgnoreCase))
                {
                    creditCardSales +=
                        sale.Total;
                }
            }

            decimal totalExpenses =
                await _db.CashMovements
                    .AsNoTracking()
                    .Where(x =>
                        x.MovementType == "EXPENSE" &&
                        x.MovementDate >= startUtc &&
                        x.MovementDate < endUtc
                    )
                    .SumAsync(x =>
                        x.Amount
                    );

            List<CreditPaymentDto> payments =
                await _quickBooksService
                    .GetCreditPaymentsListAsync(
                        startText,
                        endText
                    );

            decimal creditPayments =
                payments.Sum(x =>
                    x.TotalAmount
                );

            decimal totalSales =
                cashSales +
                checkSales +
                creditCardSales +
                creditPayments;

            return new GeneralCutDto
            {
                StartDate =
                    finalStartDate,

                EndDate =
                    finalEndDate,

                TotalExpenses =
                    totalExpenses,

                CheckSales =
                    checkSales,

                CashSales =
                    cashSales,

                CreditCardSales =
                    creditCardSales,

                CreditPayments =
                    creditPayments,

                TotalSales =
                    totalSales
            };
        }

        public async Task<ProductSalesReportResponseDto>
    GetProductSalesReportAsync(
        DateTime startDate,
        DateTime endDate,
        string? cashierName = null)
        {
            DateTime finalStartDate =
                startDate.Date;

            DateTime finalEndDate =
                endDate.Date;

            if (finalEndDate < finalStartDate)
            {
                throw new Exception(
                    "La fecha final no puede ser menor que la fecha inicial."
                );
            }

            string startText =
                finalStartDate.ToString("yyyy-MM-dd");

            string endText =
                finalEndDate.ToString("yyyy-MM-dd");

            List<InvoiceResponseDto> cashSales =
                await _quickBooksService
                    .GetSalesReceiptsList(
                        startText,
                        endText
                    );

            List<InvoiceResponseDto> creditSales =
                await _quickBooksService
                    .GetCreditInvoicesList(
                        startText,
                        endText
                    );

            List<InvoiceResponseDto> sales =
                cashSales
                    .Concat(creditSales)
                    .ToList();

            if (!string.IsNullOrWhiteSpace(cashierName))
            {
                sales =
                    sales
                        .Where(x =>
                            string.Equals(
                                x.CashierName?.Trim(),
                                cashierName.Trim(),
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .ToList();
            }

            List<QuickBooksItemReportDto> itemsCatalog =
                await _quickBooksService
                    .GetItemsForReportsAsync();

            Dictionary<string, QuickBooksItemReportDto>
                catalogById =
                    itemsCatalog
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.ItemId)
                        )
                        .GroupBy(x => x.ItemId)
                        .ToDictionary(
                            group => group.Key,
                            group => group.First()
                        );

            var accumulated =
                new Dictionary<
                    string,
                    ProductSalesReportRowDto
                >();

            foreach (InvoiceResponseDto sale in sales)
            {
                InvoiceItemsResponseDto documentItems =
                    await _quickBooksService
                        .GetDocumentItemsAsync(
                            sale.QbInvoiceId
                        );

                foreach (InvoiceItemDto line in documentItems.Items)
                {
                    string key =
                        !string.IsNullOrWhiteSpace(line.ItemId)
                            ? line.ItemId
                            : line.Description.Trim();

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    catalogById.TryGetValue(
                        line.ItemId,
                        out QuickBooksItemReportDto? catalogItem
                    );

                    decimal purchaseCost =
                        catalogItem?.PurchaseCost
                        ?? 0m;

                    decimal lineCost =
                        purchaseCost *
                        line.Quantity;

                    decimal lineSale =
                        line.Total;

                    if (!accumulated.TryGetValue(
                            key,
                            out ProductSalesReportRowDto? product))
                    {
                        product =
                            new ProductSalesReportRowDto
                            {
                                ItemId =
                                    line.ItemId,

                                ProductName =
                                    !string.IsNullOrWhiteSpace(
                                        catalogItem?.Name)
                                        ? catalogItem.Name
                                        : line.Description,

                                Brand =
                                    !string.IsNullOrWhiteSpace(
                                        catalogItem?.Brand)
                                        ? catalogItem.Brand
                                        : "Sin marca"
                            };

                        accumulated.Add(
                            key,
                            product
                        );
                    }

                    product.QuantitySold +=
                        line.Quantity;

                    product.AccumulatedCost +=
                        lineCost;

                    product.AccumulatedSale +=
                        lineSale;

                    product.Profit =
                        product.AccumulatedSale -
                        product.AccumulatedCost;
                }
            }

            return new ProductSalesReportResponseDto
            {
                Products =
                    accumulated.Values
                        .OrderBy(x => x.ProductName)
                        .ToList()
            };
        }

        private async Task<string> GetPaymentMethodAsync(string quickBooksId, string saleType)
        {
            string json =
                saleType == "credito"
                    ? await _quickBooksService
                        .GetInvoiceById(quickBooksId)
                    : await _quickBooksService
                        .GetSalesReceiptById(
                            quickBooksId
                        );

            if (string.IsNullOrWhiteSpace(json))
            {
                return saleType == "credito"
                    ? "Crédito"
                    : "No indicado";
            }

            JsonElement document =
                GetQuickBooksDocument(
                    json,
                    saleType == "credito"
                        ? "Invoice"
                        : "SalesReceipt"
                );

            return saleType == "credito"
                ? "Crédito"
                : GetPaymentMethod(document);
        }

        private static string GetPaymentMethod(
            JsonElement document)
        {
            string paymentMethod =
                GetReferenceName(
                    document,
                    "PaymentMethodRef",
                    ""
                );

            if (!string.IsNullOrWhiteSpace(
                paymentMethod))
            {
                return NormalizePaymentMethod(
                    paymentMethod
                );
            }

            string depositAccount =
                GetReferenceName(
                    document,
                    "DepositToAccountRef",
                    ""
                );

            if (depositAccount.Contains(
                "tarjeta",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Tarjeta de crédito";
            }

            if (depositAccount.Contains(
                    "cheque",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Cheque";
            }

            if (depositAccount.Contains(
                    "caja",
                    StringComparison.OrdinalIgnoreCase) ||
                depositAccount.Contains(
                    "efectivo",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Efectivo";
            }

            return "No indicado";
        }

        private static string NormalizePaymentMethod(
            string value)
        {
            if (value.Contains(
                "efect",
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

            return value;
        }

        private static string GetVoucher(
            JsonElement document)
        {
            string voucher =
                GetString(
                    document,
                    "PaymentRefNum"
                );

            if (!string.IsNullOrWhiteSpace(voucher))
            {
                return voucher;
            }

            return GetString(
                document,
                "PrivateNote"
            );
        }

        private static string GetAddress(
            JsonElement document)
        {
            string[] properties =
            {
                "BillAddr",
                "ShipAddr",
                "ShipFromAddr"
            };

            foreach (string property in properties)
            {
                if (!document.TryGetProperty(
                        property,
                        out JsonElement address))
                {
                    continue;
                }

                var parts =
                    new List<string>();

                foreach (string lineProperty in new[]
                {
                    "Line1",
                    "Line2",
                    "Line3",
                    "City",
                    "CountrySubDivisionCode",
                    "PostalCode"
                })
                {
                    string value =
                        GetString(
                            address,
                            lineProperty
                        );

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        parts.Add(value.Trim());
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join(
                        ", ",
                        parts.Distinct()
                    );
                }
            }

            return "";
        }

        private static bool ContainsDocument(
            string? json,
            string property)
        {
            return !string.IsNullOrWhiteSpace(json) &&
                   json.Contains(
                       $"\"{property}\"",
                       StringComparison.Ordinal
                   );
        }

        private static JsonElement GetQuickBooksDocument(
            string json,
            string property)
        {
            using JsonDocument document =
                JsonDocument.Parse(json);

            JsonElement queryResponse =
                document.RootElement
                    .GetProperty("QueryResponse");

            JsonElement documents =
                queryResponse
                    .GetProperty(property);

            return documents[0].Clone();
        }

        private static string GetReferenceName(
            JsonElement document,
            string property,
            string defaultValue)
        {
            if (!document.TryGetProperty(
                    property,
                    out JsonElement reference))
            {
                return defaultValue;
            }

            if (reference.TryGetProperty(
                    "name",
                    out JsonElement name))
            {
                return name.GetString()
                    ?? defaultValue;
            }

            return defaultValue;
        }

        private static string GetString(
            JsonElement document,
            string property)
        {
            if (!document.TryGetProperty(
                    property,
                    out JsonElement value))
            {
                return "";
            }

            return value.ValueKind ==
                JsonValueKind.String
                    ? value.GetString() ?? ""
                    : value.ToString();
        }

        private static DateTime GetDate(
            JsonElement document,
            string property)
        {
            string value =
                GetString(
                    document,
                    property
                );

            return DateTime.TryParse(
                value,
                out DateTime result)
                    ? result
                    : DateTime.Today;
        }
    }
}