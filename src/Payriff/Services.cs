using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Payriff;

internal sealed class Body : Dictionary<string, object?>
{
    public Body Set(string key, object? value)
    {
        if (value is null || (value is ICollection { Count: 0 }) || (value is string s && s.Length == 0))
        {
            return this;
        }
        this[key] = value;
        return this;
    }
}

internal static class Guard
{
    public const int MaxPageSize = 20;

    public static string Segment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} must not be blank", name);
        }
        return Uri.EscapeDataString(value);
    }

    public static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required", name) : value;

    public static decimal Positive(decimal value, string name)
        => value <= 0 ? throw new ArgumentException($"{name} must be greater than 0", name) : value;

    public static IEnumerable<KeyValuePair<string, string?>> Page(int page, int size)
    {
        if (page < 0)
        {
            throw new ArgumentException("Page must be >= 0", nameof(page));
        }
        if (size is < 1 or > MaxPageSize)
        {
            throw new ArgumentException($"Size must be 1-{MaxPageSize}", nameof(size));
        }
        yield return new("page", page.ToString(CultureInfo.InvariantCulture));
        yield return new("offset", size.ToString(CultureInfo.InvariantCulture));
    }

    public static string? FilterDate(DateOnly? date) => date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string? Wire(Enum? value) => value is null ? null : PayriffJson.ToWire(value);
}

public sealed class OrdersService
{
    private readonly Transport _transport;

    internal OrdersService(Transport transport) => _transport = transport;

    public async Task<CreateOrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new Body
        {
            ["amount"] = Guard.Positive(request.Amount, nameof(request.Amount)),
            ["currency"] = request.Currency,
            ["operation"] = request.Operation,
        }
            .Set("language", request.Language)
            .Set("description", request.Description)
            .Set("callbackUrl", request.CallbackUrl)
            .Set("redirectUrl", request.RedirectUrl)
            .Set("cardSave", request.CardSave)
            .Set("threeDS", request.ThreeDS)
            .Set("autoPaymentType", request.AutoPaymentType)
            .Set("installment", request.Installment)
            .Set("fullName", request.FullName)
            .Set("phoneNumber", request.PhoneNumber)
            .Set("metadata", request.Metadata)
            .Set("fields", request.Fields);
        var api = new ApiRequest(HttpMethod.Post, "/api/v3/orders")
        {
            Headers = new Dictionary<string, string?> { ["X-REQUEST-RRN"] = request.RequestRrn },
            Body = body,
        };
        return await _transport.ExecuteAsync<CreateOrderResponse>(api, cancellationToken).ConfigureAwait(false) ?? new CreateOrderResponse();
    }

    public Task<OrderInfo> GetAsync(string orderId, CancellationToken cancellationToken = default)
        => LookupAsync($"/api/v3/orders/{Guard.Segment(orderId, nameof(orderId))}", cancellationToken);

    public Task<OrderInfo> GetStatusAsync(string orderId, CancellationToken cancellationToken = default)
        => LookupAsync($"/api/v3/orders/{Guard.Segment(orderId, nameof(orderId))}/status", cancellationToken);

    public Task<OrderInfo> GetByRequestRrnAsync(string requestRrn, CancellationToken cancellationToken = default)
        => LookupAsync($"/api/v3/orders/{Guard.Segment(requestRrn, nameof(requestRrn))}/rrn", cancellationToken);

    private async Task<OrderInfo> LookupAsync(string path, CancellationToken cancellationToken)
        => await _transport.ExecuteAsync<OrderInfo>(new ApiRequest(HttpMethod.Get, path), cancellationToken).ConfigureAwait(false) ?? new OrderInfo();

    public async Task ExpireAsync(string orderId, CancellationToken cancellationToken = default)
    {
        Guard.Segment(orderId, nameof(orderId));
        var api = new ApiRequest(HttpMethod.Patch, "/api/v3/expire-status") { Query = [new("orderId", orderId)] };
        await _transport.ExecuteAsync<object>(api, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new Body { ["orderId"] = Guard.Required(request.OrderId, nameof(request.OrderId)) }
            .Set("amount", request.Amount)
            .Set("refundReason", request.RefundReason)
            .Set("callbackUrl", request.CallbackUrl);
        await _transport.ExecuteAsync<object>(new ApiRequest(HttpMethod.Post, "/api/v3/refund") { Body = body }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(CompleteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new Body { ["orderId"] = Guard.Required(request.OrderId, nameof(request.OrderId)) }
            .Set("amount", request.Amount)
            .Set("callbackUrl", request.CallbackUrl);
        await _transport.ExecuteAsync<object>(new ApiRequest(HttpMethod.Post, "/api/v3/complete") { Body = body }, cancellationToken).ConfigureAwait(false);
    }

    public Task<byte[]> DownloadReceiptAsync(string orderIdOrRrn, CancellationToken cancellationToken = default)
        => _transport.DownloadAsync(new ApiRequest(HttpMethod.Get, $"/api/v3/acquiring/receipt/{Guard.Segment(orderIdOrRrn, nameof(orderIdOrRrn))}"), cancellationToken);
}

public sealed class PaymentsService
{
    private readonly Transport _transport;
    private readonly CardEncryptor _encryptor;

    internal PaymentsService(Transport transport, CardEncryptor encryptor)
    {
        _transport = transport;
        _encryptor = encryptor;
    }

    public async Task<DirectPayResponse> DirectPayAsync(DirectPayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Card);
        var body = new Body
        {
            ["amount"] = Guard.Positive(request.Amount, nameof(request.Amount)),
            ["operation"] = request.Operation,
            ["currency"] = request.Currency,
            ["description"] = Guard.Required(request.Description, nameof(request.Description)),
        }
            .Set("callbackUrl", request.CallbackUrl)
            .Set("threeDS", request.ThreeDS)
            .Set("customFields", request.CustomFields);
        var (encryptedMessage, secretKey) = _encryptor.Encrypt(request.Card);
        body["paymentData"] = new Dictionary<string, object>
        {
            ["paymentWay"] = "DIRECT",
            ["encryptedMessage"] = encryptedMessage,
            ["cardSave"] = request.CardSave,
        };
        var api = new ApiRequest(HttpMethod.Post, "/api/v3/directPay")
        {
            Headers = new Dictionary<string, string?> { ["X-REQUEST-RRN"] = request.RequestRrn, ["x-secret-key"] = secretKey },
            Body = body,
        };
        return await _transport.ExecuteAsync<DirectPayResponse>(api, cancellationToken).ConfigureAwait(false) ?? new DirectPayResponse();
    }

    public async Task<AutoPayResponse> AutoPayAsync(AutoPayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new Body
        {
            ["cardUuid"] = Guard.Required(request.CardUuid, nameof(request.CardUuid)),
            ["amount"] = Guard.Positive(request.Amount, nameof(request.Amount)),
            ["operation"] = request.Operation,
            ["currency"] = request.Currency,
            ["description"] = Guard.Required(request.Description, nameof(request.Description)),
        }
            .Set("callbackUrl", request.CallbackUrl)
            .Set("threeDS", request.ThreeDS)
            .Set("isOneCLickPayment", request.OneClickPayment);
        var api = new ApiRequest(HttpMethod.Post, "/api/v3/autoPay")
        {
            Headers = new Dictionary<string, string?> { ["X-REQUEST-RRN"] = request.RequestRrn },
            Body = body,
        };
        return await _transport.ExecuteAsync<AutoPayResponse>(api, cancellationToken).ConfigureAwait(false) ?? new AutoPayResponse();
    }
}

public sealed class CardsService
{
    private readonly Transport _transport;

    internal CardsService(Transport transport) => _transport = transport;

    public async Task<CardSaveResponse> SaveAsync(CardSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new Body
        {
            ["customerRef"] = Guard.Required(request.CustomerRef, nameof(request.CustomerRef)),
            ["callbackUrl"] = Guard.Required(request.CallbackUrl, nameof(request.CallbackUrl)),
        }
            .Set("description", request.Description)
            .Set("language", request.Language)
            .Set("metadata", request.Metadata);
        var api = new ApiRequest(HttpMethod.Post, "/api/v3/cards/save")
        {
            Headers = new Dictionary<string, string?> { ["X-Idempotency-Key"] = request.IdempotencyKey },
            Body = body,
        };
        return await _transport.ExecuteAsync<CardSaveResponse>(api, cancellationToken).ConfigureAwait(false) ?? new CardSaveResponse();
    }

    public async Task<CardSaveDetails> GetSaveAsync(string cardSaveId, CancellationToken cancellationToken = default)
    {
        var api = new ApiRequest(HttpMethod.Get, $"/api/v3/cards/save/{Guard.Segment(cardSaveId, nameof(cardSaveId))}");
        return await _transport.ExecuteAsync<CardSaveDetails>(api, cancellationToken).ConfigureAwait(false) ?? new CardSaveDetails();
    }

    public async Task<IReadOnlyList<SavedCard>> ListAsync(string customerRef, CancellationToken cancellationToken = default)
    {
        Guard.Segment(customerRef, nameof(customerRef));
        var api = new ApiRequest(HttpMethod.Get, "/api/v3/cards/save") { Query = [new("customerRef", customerRef)] };
        return await _transport.ExecuteAsync<List<SavedCard>>(api, cancellationToken).ConfigureAwait(false) ?? [];
    }

    public async Task DeleteAsync(string cardUuid, CancellationToken cancellationToken = default)
    {
        var api = new ApiRequest(HttpMethod.Delete, $"/api/v3/cards/{Guard.Segment(cardUuid, nameof(cardUuid))}");
        await _transport.ExecuteAsync<object>(api, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class TransactionsService
{
    private readonly Transport _transport;

    internal TransactionsService(Transport transport) => _transport = transport;

    public async Task<Page<Transaction>> ListAsync(TransactionFilter? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new TransactionFilter();
        var query = new List<KeyValuePair<string, string?>>
        {
            new("orderId", filter.OrderId),
            new("rrn", filter.Rrn),
            new("status", Guard.Wire(filter.Status)),
            new("amount", filter.Amount),
            new("description", filter.Description),
            new("name", filter.Name),
            new("fullName", filter.FullName),
            new("cardNumber", filter.CardNumber),
            new("bookingId", filter.BookingId),
            new("invoiceCode", filter.InvoiceCode),
            new("from", Guard.FilterDate(filter.From)),
            new("to", Guard.FilterDate(filter.To)),
        };
        query.AddRange(Guard.Page(filter.Page, filter.Size));
        var api = new ApiRequest(HttpMethod.Get, "/api/v3/transactions") { Query = query };
        return await _transport.ExecuteAsync<Page<Transaction>>(api, cancellationToken).ConfigureAwait(false) ?? new Page<Transaction>();
    }
}

public sealed partial class PayoutsService
{
    private readonly Transport _transport;

    internal PayoutsService(Transport transport) => _transport = transport;

    public async Task<PayoutResult> CreateAsync(PayoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransferAmount < 1)
        {
            throw new ArgumentException("TransferAmount must be at least 1", nameof(request));
        }
        var body = new Body
        {
            ["transferAmount"] = request.TransferAmount,
            ["description"] = Guard.Required(request.Description, nameof(request.Description)),
            ["fullName"] = Guard.Required(request.FullName, nameof(request.FullName)),
            ["finCode"] = Guard.Required(request.FinCode, nameof(request.FinCode)),
        }
            .Set("cardPan", request.CardPan)
            .Set("bankName", request.BankName)
            .Set("cardType", request.CardType)
            .Set("requestRrn", request.RequestRrn)
            .Set("customerCode", request.CustomerCode)
            .Set("voen", request.Voen)
            .Set("birthDate", request.BirthDate)
            .Set("callbackUrl", request.CallbackUrl);
        var api = new ApiRequest(HttpMethod.Post, "/api/v3/payout")
        {
            Headers = new Dictionary<string, string?> { ["X-IDEMPOTENCY-KEY"] = request.IdempotencyKey },
            Body = body,
            MerchantEnvelope = true,
        };
        return await _transport.ExecuteAsync<PayoutResult>(api, cancellationToken).ConfigureAwait(false) ?? new PayoutResult();
    }

    public async Task<PayoutStatus> GetByRequestRrnAsync(string requestRrn, CancellationToken cancellationToken = default)
    {
        var api = new ApiRequest(HttpMethod.Get, $"/api/v3/payout/info/{Guard.Segment(requestRrn, nameof(requestRrn))}");
        return await _transport.ExecuteAsync<PayoutStatus>(api, cancellationToken).ConfigureAwait(false) ?? new PayoutStatus();
    }

    [GeneratedRegex(@"[\s-]")]
    private static partial Regex Separators();

    public async Task<string> CheckCardholderAsync(string cardPan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cardPan);
        var pan = Separators().Replace(cardPan, "");
        if (pan.Length != 16 || !pan.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("cardPan must be a 16-digit number", nameof(cardPan));
        }
        var api = new ApiRequest(HttpMethod.Post, "/api/v3/payout/check-cardholder") { Body = new Dictionary<string, string> { ["cardPan"] = pan } };
        return await _transport.ExecuteAsync<string>(api, cancellationToken).ConfigureAwait(false) ?? "";
    }

    public async Task<Page<PayoutSummary>> ListAsync(PayoutFilter? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new PayoutFilter();
        var query = new List<KeyValuePair<string, string?>>
        {
            new("rrn", filter.Rrn),
            new("status", Guard.Wire(filter.Status)),
            new("amount", filter.Amount),
            new("description", filter.Description),
            new("fullName", filter.FullName),
            new("finCode", filter.FinCode),
            new("bankSource", filter.BankSource),
            new("from", Guard.FilterDate(filter.From)),
            new("to", Guard.FilterDate(filter.To)),
        };
        query.AddRange(Guard.Page(filter.Page, filter.Size));
        var api = new ApiRequest(HttpMethod.Get, "/api/v3/payouts") { Query = query };
        return await _transport.ExecuteAsync<Page<PayoutSummary>>(api, cancellationToken).ConfigureAwait(false) ?? new Page<PayoutSummary>();
    }

    public Task<byte[]> DownloadReceiptAsync(string requestRrn, CancellationToken cancellationToken = default)
        => _transport.DownloadAsync(new ApiRequest(HttpMethod.Get, $"/api/v3/payout/receipt/{Guard.Segment(requestRrn, nameof(requestRrn))}"), cancellationToken);
}

public sealed class InvoicesService
{
    private readonly Transport _transport;

    internal InvoicesService(Transport transport) => _transport = transport;

    public async Task<Invoice> CreateAsync(InvoiceCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AmountDynamic != true && request.Amount is null)
        {
            throw new ArgumentException("Amount is required", nameof(request));
        }
        var body = new Body { ["currencyType"] = request.Currency }
            .Set("amount", request.Amount)
            .Set("amountDynamic", request.AmountDynamic)
            .Set("languageType", request.Language)
            .Set("fullName", request.FullName)
            .Set("email", request.Email)
            .Set("phoneNumber", request.PhoneNumber)
            .Set("description", request.Description)
            .Set("customMessage", request.CustomMessage)
            .Set("expireDate", request.ExpireDate?.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture))
            .Set("approveURL", request.ApproveUrl)
            .Set("cancelURL", request.CancelUrl)
            .Set("declineURL", request.DeclineUrl)
            .Set("redirectURL", request.RedirectUrl)
            .Set("installmentProductType", request.InstallmentProductType)
            .Set("installmentPeriod", request.InstallmentPeriod)
            .Set("directPay", request.DirectPay)
            .Set("sendSms", request.SendSms)
            .Set("sendWhatsapp", request.SendWhatsapp)
            .Set("sendEmail", request.SendEmail)
            .Set("metadata", request.Metadata)
            .Set("externalTransactionId", request.ExternalTransactionId);
        var api = new ApiRequest(HttpMethod.Post, "/api/v2/invoices") { Body = body, MerchantEnvelope = true };
        return await _transport.ExecuteAsync<Invoice>(api, cancellationToken).ConfigureAwait(false) ?? new Invoice();
    }

    public async Task<InvoiceDetails> GetAsync(string invoiceUuid, CancellationToken cancellationToken = default)
    {
        Guard.Segment(invoiceUuid, nameof(invoiceUuid));
        var api = new ApiRequest(HttpMethod.Post, "/api/v2/get-invoice")
        {
            Body = new Dictionary<string, string> { ["uuid"] = invoiceUuid },
            MerchantEnvelope = true,
        };
        return await _transport.ExecuteAsync<InvoiceDetails>(api, cancellationToken).ConfigureAwait(false) ?? new InvoiceDetails();
    }
}