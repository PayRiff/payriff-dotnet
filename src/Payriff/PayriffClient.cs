namespace Payriff;

public sealed class PayriffOptions
{
    public required string AppKey { get; init; }
    public string? MerchantId { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public string? CardEncryptionKey { get; init; }
    public Uri BaseUrl { get; init; } = new(PayriffClient.ProductionUrl);
}

public sealed class PayriffClient : IDisposable
{
    public const string Version = "0.1.1";
    public const string ProductionUrl = "https://api.payriff.com";

    private readonly HttpClient? _ownedHttpClient;

    public PayriffClient(string appKey)
        : this(new PayriffOptions { AppKey = appKey })
    {
    }

    public PayriffClient(PayriffOptions options)
        : this(options, null)
    {
    }

    public PayriffClient(PayriffOptions options, HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.AppKey))
        {
            throw new ArgumentException("AppKey is required", nameof(options));
        }
        if (options.Timeout <= TimeSpan.Zero || options.ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeouts must be positive", nameof(options));
        }
        var encryptor = new CardEncryptor(options.CardEncryptionKey ?? CardEncryptor.ProductionKey);
        if (httpClient is null)
        {
            _ownedHttpClient = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = options.ConnectTimeout,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            httpClient = _ownedHttpClient;
        }
        var transport = new Transport(httpClient, options.BaseUrl.ToString(), options.AppKey, options.MerchantId, options.Timeout);
        Orders = new OrdersService(transport);
        Payments = new PaymentsService(transport, encryptor);
        Cards = new CardsService(transport);
        Transactions = new TransactionsService(transport);
        Payouts = new PayoutsService(transport);
        Invoices = new InvoicesService(transport);
    }

    public OrdersService Orders { get; }
    public PaymentsService Payments { get; }
    public CardsService Cards { get; }
    public TransactionsService Transactions { get; }
    public PayoutsService Payouts { get; }
    public InvoicesService Invoices { get; }

    public void Dispose() => _ownedHttpClient?.Dispose();
}

public static class PayriffWebhook
{
    private const string NotACallback = "Not a Payriff order callback";

    public static OrderInfo ParseOrderCallback(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object
                || !root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != System.Text.Json.JsonValueKind.Object
                || !payload.TryGetProperty("orderId", out var orderId)
                || orderId.ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                throw new ArgumentException(NotACallback, nameof(body));
            }
            return System.Text.Json.JsonSerializer.Deserialize<OrderInfo>(payload, PayriffJson.Options)!;
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new ArgumentException(NotACallback, nameof(body), e);
        }
    }
}