namespace Payriff.Tests;

public sealed class TransportTests : IDisposable
{
    private readonly MockServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task UnwrapsPayloadOnSuccess()
    {
        _server.Ok("GET", "/api/v3/orders/ORD-1", new { orderId = "ORD-1", paymentStatus = "APPROVED" });

        var order = await _server.Client().Orders.GetAsync("ORD-1");

        Assert.Equal("ORD-1", order.OrderId);
        Assert.Equal(PaymentStatus.Approved, order.PaymentStatus);
    }

    [Fact]
    public async Task SendsAuthAndClientHeaders()
    {
        _server.Ok("GET", "/api/v3/orders/ORD-1", new { orderId = "ORD-1" });

        await _server.Client().Orders.GetAsync("ORD-1");

        var sent = _server.Last;
        Assert.Equal("app-key", sent.Header("Authorization"));
        Assert.Equal($"payriff-dotnet/{PayriffClient.Version}", sent.Header("User-Agent"));
        Assert.Equal("application/json", sent.Header("Accept"));
        Assert.Null(sent.Header("Content-Type"));
    }

    [Fact]
    public async Task EncodesPathSegmentsAndQuery()
    {
        _server.Ok("PATCH", "/api/v3/expire-status", null);
        _server.Ok("GET", "/api/v3/orders/rrn%201%2F2/rrn", new { orderId = "ORD-1" });
        var client = _server.Client();

        await client.Orders.ExpireAsync("ORD 1/2+3");
        Assert.Equal("/api/v3/expire-status?orderId=ORD%201%2F2%2B3", _server.Last.Url);

        await client.Orders.GetByRequestRrnAsync("rrn 1/2");
        Assert.Equal("/api/v3/orders/rrn%201%2F2/rrn", _server.Last.Url);
    }

    [Fact]
    public async Task RejectsBlankPathSegmentBeforeSending()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => _server.Client().Orders.GetAsync(" "));

        Assert.StartsWith("orderId must not be blank", error.Message);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task MerchantEnvelopeRequiresMerchantId()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _server.Client(merchantId: null).Invoices.GetAsync("inv-1"));

        Assert.Equal("MerchantId must be configured in PayriffOptions for this operation", error.Message);
        Assert.Empty(_server.Requests);
    }

    [Theory]
    [InlineData(200, "15000", typeof(ApiException))]
    [InlineData(401, "14010", typeof(AuthenticationException))]
    [InlineData(401, "14013", typeof(AuthenticationException))]
    [InlineData(200, "14014", typeof(AuthenticationException))]
    [InlineData(401, "14015", typeof(AuthenticationException))]
    [InlineData(400, "15400", typeof(ValidationException))]
    [InlineData(400, "99999", typeof(ValidationException))]
    [InlineData(403, "99999", typeof(AuthenticationException))]
    [InlineData(402, "01000", typeof(RequestRejectedException))]
    [InlineData(402, "01200", typeof(InsufficientBalanceException))]
    [InlineData(402, "01300", typeof(PayoutLimitException))]
    [InlineData(402, "01400", typeof(PayoutLimitException))]
    [InlineData(402, "01500", typeof(PayoutLimitException))]
    [InlineData(500, "15000", typeof(ApiException))]
    [InlineData(503, "15000", typeof(ApiException))]
    public async Task MapsFailuresToTypedExceptions(int status, string code, Type expected)
    {
        _server.Stub("GET", "/api/v3/orders/ORD-1", status, body: new { code, message = "Failure reason", responseId = "resp-1" });

        var error = await Assert.ThrowsAnyAsync<ApiException>(() => _server.Client().Orders.GetAsync("ORD-1"));

        Assert.Equal(expected, error.GetType());
        Assert.Equal("Failure reason", error.Message);
        Assert.Equal((status, code, "resp-1"), (error.HttpStatus, error.Code, error.ResponseId));
    }

    [Fact]
    public async Task MissingMessageFallsBackToHttpStatus()
    {
        _server.Stub("GET", "/api/v3/orders/ORD-1", 500, body: new { code = "15000" });

        var error = await Assert.ThrowsAsync<ApiException>(() => _server.Client().Orders.GetAsync("ORD-1"));

        Assert.Equal("Payriff request failed (HTTP 500)", error.Message);
    }

    [Fact]
    public async Task NonJsonErrorBodyKeepsStatusAndTruncates()
    {
        _server.Stub("GET", "/api/v3/orders/ORD-1", 502, new() { ["Content-Type"] = "text/html" }, new string('x', 600));

        var error = await Assert.ThrowsAsync<ApiException>(() => _server.Client().Orders.GetAsync("ORD-1"));

        Assert.Equal(502, error.HttpStatus);
        Assert.Null(error.Code);
        Assert.Equal(new string('x', 500) + "...", error.Message);
    }

    [Fact]
    public async Task SuccessStatusWithoutEnvelopeIsRejected()
    {
        _server.Stub("GET", "/api/v3/orders/ORD-1", body: new { orderId = "ORD-1" });

        var error = await Assert.ThrowsAsync<ApiException>(() => _server.Client().Orders.GetAsync("ORD-1"));

        Assert.Equal("Unexpected response from Payriff", error.Message);
    }

    [Fact]
    public async Task RedirectsAreNotFollowed()
    {
        _server.Stub("GET", "/api/v3/orders/ORD-1", 302, new() { ["Location"] = "/api/v3/orders/OTHER" }, new { code = "15000", message = "Redirect" });

        var error = await Assert.ThrowsAsync<ApiException>(() => _server.Client().Orders.GetAsync("ORD-1"));

        Assert.Equal("Redirect", error.Message);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task DownloadReturnsBytes()
    {
        _server.Stub("GET", "/api/v3/acquiring/receipt/ORD-1", headers: new() { ["Content-Type"] = "application/pdf" }, body: "%PDF-1.7"u8.ToArray());

        var pdf = await _server.Client().Orders.DownloadReceiptAsync("ORD-1");

        Assert.Equal("%PDF-1.7"u8.ToArray(), pdf);
        Assert.Equal("application/pdf, application/json", _server.Last.Header("Accept"));
    }

    [Fact]
    public async Task DownloadMapsEnvelopeError()
    {
        _server.Stub("GET", "/api/v3/acquiring/receipt/ORD-1", 400, body: new { code = "15400", message = "Receipt not found" });

        await Assert.ThrowsAsync<ValidationException>(() => _server.Client().Orders.DownloadReceiptAsync("ORD-1"));
    }

    [Fact]
    public async Task ConnectionFailureRaisesConnectionException()
    {
        using var client = new PayriffClient(new PayriffOptions { AppKey = "app-key", BaseUrl = new Uri("http://127.0.0.1:1") });

        var error = await Assert.ThrowsAsync<PayriffConnectionException>(() => client.Orders.GetAsync("ORD-1"));

        Assert.Equal(0, error.HttpStatus);
    }

    [Fact]
    public async Task TimeoutRaisesConnectionException()
    {
        using var client = new PayriffClient(new PayriffOptions
        {
            AppKey = "app-key",
            BaseUrl = new Uri("http://10.255.255.1"),
            Timeout = TimeSpan.FromMilliseconds(100),
        });

        await Assert.ThrowsAsync<PayriffConnectionException>(() => client.Orders.GetAsync("ORD-1"));
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsConnectionFailure()
    {
        using var client = new PayriffClient(new PayriffOptions { AppKey = "app-key", BaseUrl = new Uri("http://10.255.255.1") });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Orders.GetAsync("ORD-1", cts.Token));
    }

    [Fact]
    public void ClientValidation()
    {
        Assert.StartsWith("AppKey is required", Assert.Throws<ArgumentException>(() => new PayriffClient(" ")).Message);
        var keyError = Assert.Throws<ArgumentException>(() => new PayriffClient(new PayriffOptions { AppKey = "k", CardEncryptionKey = "nope" }));
        Assert.StartsWith("Invalid RSA public key", keyError.Message);

        using var client = new PayriffClient("k");
        Assert.NotNull(client.Orders);
        Assert.NotNull(client.Payments);
        Assert.NotNull(client.Cards);
        Assert.NotNull(client.Transactions);
        Assert.NotNull(client.Payouts);
        Assert.NotNull(client.Invoices);
    }
}