using System.Text.Json.Nodes;

namespace Payriff.Tests;

public sealed class ApiTests : IDisposable
{
    private const string OrderInfoJson = """
        {"orderId":"ORD-1","amount":10.50,"currencyType":"AZN","merchantName":"Shop","commission":0.20,"commissionRate":2.00,
         "operationType":"PURCHASE","paymentStatus":"APPROVED","auto":false,"createdDate":"2026-10-01T14:05:09.123456",
         "description":"Order #1","metadata":"{\"k\":\"v\"}",
         "transactions":[{"uuid":"6f1c2a4e-0b7d-4c3e-9a51-2d8e7f6b1c90","createdDate":"2026-10-01T14:05:10.000001",
         "status":"APPROVED","channel":"KAPITAL_BANK","cardDetails":{"maskedPan":"416974******1979","brand":"VISA","bcryptedCardPan":"$2a$x"},
         "installment":{"type":"BIRKART","period":"PERIOD_3"}}]}
        """;

    private readonly MockServer _server = new();

    public void Dispose() => _server.Dispose();

    private static void AssertJson(string expected, JsonNode actual)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"expected {expected}\n  actual {actual.ToJsonString()}");

    [Fact]
    public async Task OrdersCreateSendsBodyAndRrnHeader()
    {
        _server.OkRaw("POST", "/api/v3/orders", """
            {"orderId":"ORD-1","paymentUrl":"https://pay.payriff.com/ORD-1","transactionId":77,"comissionRate":2.5,
             "amount":10.00,"fee":0.25,"totalAmount":10.25}
            """);

        var response = await _server.Client().Orders.CreateAsync(new CreateOrderRequest
        {
            Amount = 10.00m,
            Language = Language.Az,
            Description = "Order #1",
            CallbackUrl = "https://shop.az/cb",
            Installment = new Installment(InstallmentProductType.Birkart, InstallmentPeriod.Period3),
            Metadata = new Dictionary<string, string> { ["cartId"] = "c-9" },
            RequestRrn = "rrn-1",
        });

        var sent = _server.Last;
        Assert.Equal("rrn-1", sent.Header("X-REQUEST-RRN"));
        Assert.StartsWith("application/json", sent.Header("Content-Type"));
        AssertJson("""
            {"amount":10.00,"currency":"AZN","operation":"PURCHASE","language":"AZ","description":"Order #1",
             "callbackUrl":"https://shop.az/cb","installment":{"type":"BIRKART","period":"PERIOD_3"},"metadata":{"cartId":"c-9"}}
            """, sent.Json);
        Assert.Equal(("ORD-1", 77L, 2.5m, 10.25m), (response.OrderId, response.TransactionId, response.CommissionRate, response.TotalAmount));
    }

    [Fact]
    public async Task OrdersCreateOmitsRrnHeaderAndEmptyMaps()
    {
        _server.Ok("POST", "/api/v3/orders", new { orderId = "ORD-1" });

        await _server.Client().Orders.CreateAsync(new CreateOrderRequest { Amount = 1, Metadata = new Dictionary<string, string>() });

        Assert.Null(_server.Last.Header("X-REQUEST-RRN"));
        AssertJson("""{"amount":1,"currency":"AZN","operation":"PURCHASE"}""", _server.Last.Json);
    }

    [Fact]
    public async Task OrdersCreateRejectsNonPositiveAmount()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _server.Client().Orders.CreateAsync(new CreateOrderRequest { Amount = 0 }));
        Assert.Empty(_server.Requests);
    }

    [Theory]
    [InlineData("/api/v3/orders/ORD-1", false)]
    [InlineData("/api/v3/orders/ORD-1/status", true)]
    public async Task OrdersLookupsParseOrderInfo(string path, bool status)
    {
        _server.OkRaw("GET", path, OrderInfoJson);
        var client = _server.Client();

        var order = status ? await client.Orders.GetStatusAsync("ORD-1") : await client.Orders.GetAsync("ORD-1");

        Assert.Equal(path, _server.Last.Url);
        Assert.Equal(("ORD-1", 10.50m, Currency.Azn, PaymentStatus.Approved, Operation.Purchase), (order.OrderId, order.Amount!.Value, order.Currency!.Value, order.PaymentStatus, order.OperationType!.Value));
        Assert.Equal(new DateTime(2026, 10, 1, 14, 5, 9, 123).AddTicks(4560), order.CreatedDate);
        Assert.Equal("""{"k":"v"}""", order.Metadata);
        var tx = Assert.Single(order.Transactions);
        Assert.Equal(("KAPITAL_BANK", "416974******1979", "VISA", InstallmentPeriod.Period3), (tx.Channel, tx.CardDetails!.MaskedPan, tx.CardDetails.Brand, tx.Installment!.Period));
    }

    [Fact]
    public async Task UnknownValuesDoNotBreakParsing()
    {
        _server.OkRaw("GET", "/api/v3/orders/ORD-1", """
            {"orderId":"ORD-1","paymentStatus":"SOMETHING_NEW","currencyType":"GBP","operationType":42,
             "createdDate":"not a date","transactions":null,"brandNewField":{"x":1}}
            """);

        var order = await _server.Client().Orders.GetAsync("ORD-1");

        Assert.Equal(PaymentStatus.Unknown, order.PaymentStatus);
        Assert.Null(order.Currency);
        Assert.Null(order.OperationType);
        Assert.Null(order.CreatedDate);
        Assert.Null(order.Transactions);
    }

    [Fact]
    public async Task OrdersRefundAndComplete()
    {
        _server.Ok("POST", "/api/v3/refund", null);
        _server.Ok("POST", "/api/v3/complete", null);
        var client = _server.Client();

        await client.Orders.RefundAsync(new RefundRequest { OrderId = "ORD-1", Amount = 5.00m, RefundReason = "damaged" });
        AssertJson("""{"orderId":"ORD-1","amount":5.00,"refundReason":"damaged"}""", _server.Last.Json);

        await client.Orders.CompleteAsync(new CompleteRequest { OrderId = "ORD-1", Amount = 7.5m });
        AssertJson("""{"orderId":"ORD-1","amount":7.5}""", _server.Last.Json);

        await Assert.ThrowsAsync<ArgumentException>(() => client.Orders.RefundAsync(new RefundRequest { OrderId = " " }));
    }

    [Fact]
    public async Task DirectPayEncryptsCardAndSendsSecretKey()
    {
        _server.OkRaw("POST", "/api/v3/directPay", """
            {"orderId":"ORD-1","threeDS":true,"redirect":true,"redirectUrl":"https://acs.bank/3ds",
             "transactionResponse":{"status":"CREATED","requestRrn":"req-1"}}
            """);

        var response = await _server.Client(cardEncryptionKey: CardKeys.PublicBase64).Payments.DirectPayAsync(new DirectPayRequest
        {
            Amount = 1.00m,
            Description = "Order #1",
            CallbackUrl = "https://shop.az/cb",
            CardSave = true,
            RequestRrn = "rrn-1",
            Card = new CardData("4169741330151979", "JOHN DOE", "11", "2027", "123"),
        });

        var sent = _server.Last;
        Assert.Equal("rrn-1", sent.Header("X-REQUEST-RRN"));
        Assert.DoesNotContain("4169741330151979", sent.BodyText);
        Assert.DoesNotContain("JOHN DOE", sent.BodyText);
        var body = sent.Json.AsObject();
        var paymentData = body["paymentData"]!.AsObject();
        body.Remove("paymentData");
        AssertJson("""{"amount":1.00,"operation":"PURCHASE","currency":"AZN","description":"Order #1","callbackUrl":"https://shop.az/cb"}""", body);
        Assert.Equal("DIRECT", (string?)paymentData["paymentWay"]);
        Assert.True((bool)paymentData["cardSave"]!);
        Assert.Equal("""{"pan":"4169741330151979","cardHolder":"JOHN DOE","expiryYear":"2027","expiryMonth":"11","cvv":"123"}""",
            CardKeys.Decrypt(sent.Header("x-secret-key")!, (string)paymentData["encryptedMessage"]!));
        Assert.Equal(("ORD-1", true, true, "https://acs.bank/3ds", "CREATED"),
            (response.OrderId, response.ThreeDS, response.Redirect, response.RedirectUrl, response.Transaction!.Status));
    }

    [Fact]
    public async Task AutoPaySendsExplicitCurrencyAndOneClickFlag()
    {
        _server.OkRaw("POST", "/api/v3/autoPay", """
            {"orderId":"ORD-2","amount":3.00,"paymentStatus":"APPROVED","auto":true,"createdDate":"2026-10-01T10:00:00.000000",
             "transactionResponseDto":{"threeDS":false}}
            """);

        var response = await _server.Client().Payments.AutoPayAsync(new AutoPayRequest
        {
            CardUuid = "card-uuid-1",
            Amount = 3.00m,
            Description = "Subscription",
            OneClickPayment = true,
            RequestRrn = "rrn-2",
        });

        Assert.Equal("rrn-2", _server.Last.Header("X-REQUEST-RRN"));
        AssertJson("""{"cardUuid":"card-uuid-1","amount":3.00,"operation":"PURCHASE","currency":"AZN","description":"Subscription","isOneCLickPayment":true}""", _server.Last.Json);
        Assert.Equal((PaymentStatus.Approved, true, false), (response.PaymentStatus, response.Auto, response.TransactionResponse!.ThreeDS));
    }

    [Fact]
    public async Task CardsSaveGetListDelete()
    {
        _server.Ok("POST", "/api/v3/cards/save", new { cardSaveId = "cs-1", status = "CREATED", amount = 0.10 });
        _server.OkRaw("GET", "/api/v3/cards/save/cs-1", """{"cardSaveId":"cs-1","status":"VERIFIED","cardUuid":"card-1","verifiedDate":"2026-10-01T10:01:30.5"}""");
        _server.Ok("GET", "/api/v3/cards/save?customerRef=cust%201", new[] { new { cardUuid = "card-1" }, new { cardUuid = "card-2" } });
        _server.Ok("DELETE", "/api/v3/cards/card-1", true);
        var client = _server.Client();

        var saved = await client.Cards.SaveAsync(new CardSaveRequest
        {
            CustomerRef = "cust-1", CallbackUrl = "https://shop.az/cards/cb", Language = Language.Az, IdempotencyKey = "idem-1",
        });
        Assert.Equal(("cs-1", CardSaveStatus.Created, 0.10m), (saved.CardSaveId, saved.Status, saved.Amount!.Value));
        Assert.Equal("idem-1", _server.Last.Header("X-Idempotency-Key"));
        AssertJson("""{"customerRef":"cust-1","callbackUrl":"https://shop.az/cards/cb","language":"AZ"}""", _server.Last.Json);

        var details = await client.Cards.GetSaveAsync("cs-1");
        Assert.Equal((CardSaveStatus.Verified, "card-1", 500), (details.Status, details.CardUuid, details.VerifiedDate!.Value.Millisecond));

        var cards = await client.Cards.ListAsync("cust 1");
        Assert.Equal(["card-1", "card-2"], cards.Select(c => c.CardUuid));

        await client.Cards.DeleteAsync("card-1");
        Assert.Equal("DELETE", _server.Last.Method);
    }

    [Fact]
    public async Task CardsListReturnsEmptyForNullPayload()
    {
        _server.Ok("GET", "/api/v3/cards/save", null);

        Assert.Empty(await _server.Client().Cards.ListAsync("cust-1"));
    }

    [Fact]
    public async Task TransactionsListSendsFilterAndParsesPage()
    {
        _server.OkRaw("GET", "/api/v3/transactions?status=PREAUTH_APPROVED&from=01.09.2026&to=30.09.2026&page=1&offset=20", """
            {"content":[{"id":5,"orderId":"ORD-1","amount":10.00,"currencyType":"AZN","paymentStatus":"PREAUTH_APPROVED",
             "card_brand":"VISA","payment_way":"DIRECT","extra_payment":0.50,"createdDate":"2026-09-15 10:00:00"}],
             "totalElements":41,"totalPages":3,"number":1,"size":20,"first":false,"last":false}
            """);

        var page = await _server.Client().Transactions.ListAsync(new TransactionFilter
        {
            Status = PaymentStatus.PreauthApproved,
            From = new DateOnly(2026, 9, 1),
            To = new DateOnly(2026, 9, 30),
            Page = 1,
            Size = 20,
        });

        var tx = Assert.Single(page.Content);
        Assert.Equal((41L, false), (page.TotalElements, page.Last));
        Assert.Equal(("VISA", "DIRECT", 0.50m, PaymentStatus.PreauthApproved, "2026-09-15 10:00:00"),
            (tx.CardBrand, tx.PaymentWay, tx.ExtraPayment!.Value, tx.PaymentStatus, tx.CreatedDate));
    }

    [Fact]
    public async Task TransactionsDefaultsAndPageSizeCap()
    {
        _server.Ok("GET", "/api/v3/transactions", new { content = Array.Empty<object>() });
        var client = _server.Client();

        await client.Transactions.ListAsync();
        Assert.Equal("/api/v3/transactions?page=0&offset=10", _server.Last.Url);

        foreach (var size in new[] { 0, 21, -1 })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => client.Transactions.ListAsync(new TransactionFilter { Size = size }));
        }
        await Assert.ThrowsAsync<ArgumentException>(() => client.Transactions.ListAsync(new TransactionFilter { Page = -1 }));
    }

    [Fact]
    public async Task PayoutsCreateWrapsBodyInMerchantEnvelope()
    {
        _server.OkRaw("POST", "/api/v3/payout", """{"_final":"true","state":"SUCCESS","currentDepositBalance":975.00,"walletHistoryId":321,"bankName":"KAPITAL"}""");

        var result = await _server.Client().Payouts.CreateAsync(new PayoutRequest
        {
            TransferAmount = 25.00m,
            Description = "Refund to customer",
            FullName = "JOHN DOE",
            FinCode = "1AB2C3D",
            CardPan = "4169741330151979",
            RequestRrn = "po-1",
            IdempotencyKey = "idem-po-1",
        });

        Assert.Equal("idem-po-1", _server.Last.Header("X-IDEMPOTENCY-KEY"));
        AssertJson("""
            {"merchant":"ES1000000","body":{"transferAmount":25.00,"description":"Refund to customer","fullName":"JOHN DOE",
             "finCode":"1AB2C3D","cardPan":"4169741330151979","requestRrn":"po-1"}}
            """, _server.Last.Json);
        Assert.Equal(("true", "SUCCESS", 321L, 975.00m), (result.FinalState, result.State, result.WalletHistoryId!.Value, result.CurrentDepositBalance!.Value));
    }

    [Fact]
    public async Task PayoutsErrorsAndValidation()
    {
        _server.Stub("POST", "/api/v3/payout", 402, body: new { code = "01200", message = "Insufficient wallet balance" });
        var client = _server.Client();
        var request = new PayoutRequest { TransferAmount = 25, Description = "d", FullName = "f", FinCode = "c" };

        await Assert.ThrowsAsync<InsufficientBalanceException>(() => client.Payouts.CreateAsync(request));

        var small = new PayoutRequest { TransferAmount = 0.99m, Description = "d", FullName = "f", FinCode = "c" };
        Assert.StartsWith("TransferAmount must be at least 1", (await Assert.ThrowsAsync<ArgumentException>(() => client.Payouts.CreateAsync(small))).Message);
        Assert.StartsWith("cardPan must be a 16-digit number", (await Assert.ThrowsAsync<ArgumentException>(() => client.Payouts.CheckCardholderAsync("4169"))).Message);
    }

    [Fact]
    public async Task PayoutsLookupsAndReceipt()
    {
        _server.OkRaw("GET", "/api/v3/payout/info/po-1", """{"state":"IN_PROGRESS","transferAmount":25.00,"createdDate":"2026-10-01T10:00:00.000+00:00","formattedDate":"01.10.2026 10:00:00"}""");
        _server.Ok("POST", "/api/v3/payout/check-cardholder", "J*** D**");
        _server.OkRaw("GET", "/api/v3/payouts?status=SUCCESS&page=0&offset=10", """{"content":[{"id":1,"requestRrn":"po-1","state":"SUCCESS","createdDate":"2026-10-01T10:00:00"}],"totalElements":1}""");
        _server.Stub("GET", "/api/v3/payout/receipt/po-1", headers: new() { ["Content-Type"] = "application/pdf" }, body: "%PDF-"u8.ToArray());
        var client = _server.Client();

        var status = await client.Payouts.GetByRequestRrnAsync("po-1");
        Assert.Equal((TransferState.InProgress, "01.10.2026 10:00:00"), (status.State, status.FormattedDate));

        Assert.Equal("J*** D**", await client.Payouts.CheckCardholderAsync("4169 7413 3015 1979"));
        AssertJson("""{"cardPan":"4169741330151979"}""", _server.Last.Json);

        var page = await client.Payouts.ListAsync(new PayoutFilter { Status = TransferState.Success });
        Assert.Equal(("po-1", TransferState.Success), (page.Content[0].RequestRrn, page.Content[0].State));

        Assert.Equal("%PDF-"u8.ToArray(), await client.Payouts.DownloadReceiptAsync("po-1"));
    }

    [Fact]
    public async Task InvoicesCreateAndGet()
    {
        _server.OkRaw("POST", "/api/v2/invoices", """
            {"id":9,"invoiceUuid":"inv-uuid-1","invoiceStatus":"PENDING","paymentUrl":"https://pay.payriff.com/i/inv?type=preview",
             "amount":15.00,"currencyType":"AZN","languageType":"AZ","expireDate":"2026-10-08T23:59:00","approveURL":"https://shop.az/ok"}
            """);
        _server.OkRaw("POST", "/api/v2/get-invoice", """{"invoiceUuid":"inv-uuid-1","invoiceStatus":"COMPLETE","paymentDay":"2026-10-02","createdDate":"2026-10-01T09:00:00.123"}""");
        var client = _server.Client();

        var invoice = await client.Invoices.CreateAsync(new InvoiceCreateRequest
        {
            Amount = 15.00m,
            Language = Language.Az,
            FullName = "JOHN DOE",
            PhoneNumber = "+994501234567",
            Description = "Consultation",
            ExpireDate = new DateTime(2026, 10, 8, 23, 59, 0),
            ApproveUrl = "https://shop.az/ok",
            SendSms = false,
            Metadata = new Dictionary<string, string> { ["bookingRef"] = "B-77" },
        });

        AssertJson("""
            {"merchant":"ES1000000","body":{"currencyType":"AZN","amount":15.00,"languageType":"AZ","fullName":"JOHN DOE",
             "phoneNumber":"+994501234567","description":"Consultation","expireDate":"2026-10-08T23:59:00",
             "approveURL":"https://shop.az/ok","sendSms":false,"metadata":{"bookingRef":"B-77"}}}
            """, _server.Last.Json);
        Assert.Equal(("inv-uuid-1", InvoiceStatus.Pending, Currency.Azn, Language.Az, "https://shop.az/ok", 23),
            (invoice.InvoiceUuid, invoice.Status, invoice.Currency!.Value, invoice.Language!.Value, invoice.ApproveUrl, invoice.ExpireDate!.Value.Hour));

        var details = await client.Invoices.GetAsync("inv-uuid-1");
        AssertJson("""{"merchant":"ES1000000","body":{"uuid":"inv-uuid-1"}}""", _server.Last.Json);
        Assert.Equal((InvoiceStatus.Complete, 2), (details.Status, details.PaymentDay!.Value.Day));

        await Assert.ThrowsAsync<ArgumentException>(() => client.Invoices.CreateAsync(new InvoiceCreateRequest()));
        _server.Ok("POST", "/api/v2/invoices", new { invoiceUuid = "inv-2" });
        await client.Invoices.CreateAsync(new InvoiceCreateRequest { AmountDynamic = true });
        AssertJson("""{"merchant":"ES1000000","body":{"currencyType":"AZN","amountDynamic":true}}""", _server.Last.Json);
    }

    [Fact]
    public void ParsesOrderCallback()
    {
        const string body = """
            {"payload":{"orderId":"ORD-1","invoiceUuid":"inv-1","amount":10.5,"currencyType":"AZN","paymentStatus":"APPROVED",
             "operationType":"PURCHASE","auto":false,"createdDate":"2026-10-01T14:05:09.123","customFields":{"x":"y"},
             "transactions":[{"uuid":"6f1c2a4e-0b7d-4c3e-9a51-2d8e7f6b1c90","status":"APPROVED"}]},
             "code":"00000","message":"Operation performed successfully","route":"/dashboard","responseId":"http-nio-1"}
            """;

        var order = PayriffWebhook.ParseOrderCallback(body);

        Assert.Equal(("ORD-1", PaymentStatus.Approved, "inv-1", 123), (order.OrderId, order.PaymentStatus, order.InvoiceUuid, order.CreatedDate!.Value.Millisecond));
        Assert.Single(order.Transactions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"payload":null}""")]
    [InlineData("""{"payload":{"amount":1}}""")]
    [InlineData("[]")]
    [InlineData("""{"payload":[]}""")]
    public void RejectsNonCallbackBodies(string body)
    {
        var error = Assert.Throws<ArgumentException>(() => PayriffWebhook.ParseOrderCallback(body));

        Assert.StartsWith("Not a Payriff order callback", error.Message);
    }
}