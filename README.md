# Payriff .NET SDK

.NET client for the Payriff merchant API: orders, saved-card payments, saved cards,
transactions, payouts and invoices.

- .NET 8 or newer
- No dependencies outside the .NET runtime
- Talks to `https://api.payriff.com` (Azerbaijan) or `https://api.gccpay.sa` (Saudi Arabia)

## Installation

```bash
dotnet add package Payriff
```

## Quick start

```csharp
using Payriff;

using var payriff = new PayriffClient(Environment.GetEnvironmentVariable("PAYRIFF_APP_KEY")!);

var order = await payriff.Orders.CreateAsync(new CreateOrderRequest
{
    Amount = 10.00m,
    Description = "Order #1001",
    CallbackUrl = "https://shop.example/payriff/callback",
    RequestRrn = "order-1001",
});

// Send the customer to the hosted payment page
return Redirect(order.PaymentUrl!);
```

Create one `PayriffClient` and reuse it (register it as a singleton). It is thread-safe. Every call
is async and accepts a `CancellationToken`.

## Configuration

```csharp
builder.Services.AddSingleton(new PayriffClient(new PayriffOptions
{
    AppKey = builder.Configuration["Payriff:AppKey"]!,
    MerchantId = builder.Configuration["Payriff:MerchantId"],   // payouts and invoices
}));
```

| Option | Required | Default | Notes |
|---|---|---|---|
| `AppKey` | yes | — | Application key from the Payriff dashboard. |
| `MerchantId` | for payouts and invoices | — | Merchant ID (e.g. `ES1000000`). |
| `Timeout` | no | 60 s | Total request timeout. Bank operations can take tens of seconds. |
| `ConnectTimeout` | no | 10 s | Connection timeout. |
| `Region` | no | `Region.Azerbaijan` | API region. Sets the base URL and the default currency. |
| `BaseUrl` | no | from `Region` | Override the API URL, e.g. for testing. The currency still comes from `Region`. |

You can also pass your own `HttpClient` (`new PayriffClient(options, httpClient)`), for example one
from `IHttpClientFactory`. Configure its handler with `AllowAutoRedirect = false`; Payriff signals some
outcomes with redirect status codes that must not be followed.

Keep the app key in user secrets, environment variables or a vault. Never commit it.

### Saudi Arabia

```csharp
using var payriff = new PayriffClient(new PayriffOptions
{
    AppKey = Environment.GetEnvironmentVariable("PAYRIFF_APP_KEY")!,
    Region = Region.Saudi,
});
```

| Region | API | Default currency |
|---|---|---|
| `Region.Azerbaijan` | `https://api.payriff.com` | `Currency.Azn` |
| `Region.Saudi` | `https://api.gccpay.sa` | `Currency.Sar` |

Use the app key from the dashboard of the same region (`dashboard.gccpay.sa` for Saudi Arabia).
A `Currency` set on a request always wins over the region default.

## Orders

```csharp
var created = await payriff.Orders.CreateAsync(new CreateOrderRequest
{
    Amount = 25.00m,
    Currency = Currency.Azn,           // default: the region currency
    Operation = Operation.PreAuth,     // default Purchase
    Language = Language.Az,
    Description = "Booking #77",
    CallbackUrl = "https://shop.example/payriff/callback",
    Metadata = new Dictionary<string, string> { ["bookingId"] = "77" },
});

var info = await payriff.Orders.GetAsync(created.OrderId);
var byRef = await payriff.Orders.GetByRequestRrnAsync("order-1001");   // the RequestRrn you sent on create

await payriff.Orders.CompleteAsync(new CompleteRequest { OrderId = created.OrderId, Amount = 25.00m });
await payriff.Orders.RefundAsync(new RefundRequest { OrderId = created.OrderId, Amount = 5.00m, RefundReason = "Partial return" });
await payriff.Orders.ExpireAsync(created.OrderId);                    // cancel an unpaid order

byte[] receiptPdf = await payriff.Orders.DownloadReceiptAsync(created.OrderId);
```

`OrderInfo.PaymentStatus` is a `PaymentStatus` such as `Approved`, `Declined`, `PreauthApproved` or
`Refunded`. If Payriff adds a status that this SDK version does not know, it reads as
`PaymentStatus.Unknown` instead of failing.

## Charging a saved card

```csharp
var charge = await payriff.Payments.AutoPayAsync(new AutoPayRequest
{
    CardUuid = savedCardUuid,
    Amount = 9.99m,
    Description = "Monthly subscription",
    RequestRrn = "sub-2026-10",
});
```

## Saved cards

```csharp
var session = await payriff.Cards.SaveAsync(new CardSaveRequest
{
    CustomerRef = "customer-42",
    CallbackUrl = "https://shop.example/payriff/card-saved",
    IdempotencyKey = Guid.NewGuid().ToString(),
});
// Redirect the customer to session.PaymentUrl to verify the card.

var details = await payriff.Cards.GetSaveAsync(session.CardSaveId);
if (details.Status == CardSaveStatus.Verified)
{
    var cardUuid = details.CardUuid;   // store it; use with AutoPayAsync
}

var cards = await payriff.Cards.ListAsync("customer-42");
await payriff.Cards.DeleteAsync(cards[0].CardUuid);
```

## Transactions

```csharp
var page = await payriff.Transactions.ListAsync(new TransactionFilter
{
    Status = PaymentStatus.Approved,
    From = new DateOnly(2026, 9, 1),
    To = new DateOnly(2026, 9, 30),
    Page = 0,
    Size = 20,                         // server maximum is 20
});

foreach (var tx in page.Content)
{
    Console.WriteLine($"{tx.OrderId} {tx.Amount}");
}
```

## Payouts

Payouts require `MerchantId` in the options.

```csharp
var maskedName = await payriff.Payouts.CheckCardholderAsync("4169741330151979");   // e.g. "J*** D**"

var payout = await payriff.Payouts.CreateAsync(new PayoutRequest
{
    TransferAmount = 50.00m,           // minimum 1
    Description = "Refund for order #1001",
    FullName = "JOHN DOE",
    FinCode = "1AB2C3D",
    CardPan = "4169741330151979",
    RequestRrn = "payout-1001",
    IdempotencyKey = "payout-1001",
});

var status = await payriff.Payouts.GetByRequestRrnAsync("payout-1001");
var history = await payriff.Payouts.ListAsync(new PayoutFilter { Status = TransferState.Success });
var receipt = await payriff.Payouts.DownloadReceiptAsync("payout-1001");
```

## Invoices

Invoices require `MerchantId` in the options.

```csharp
var invoice = await payriff.Invoices.CreateAsync(new InvoiceCreateRequest
{
    Amount = 15.00m,
    FullName = "JOHN DOE",
    PhoneNumber = "+994501234567",
    Description = "Consultation",
    ExpireDate = DateTime.Now.AddDays(7),
    SendSms = true,
});

var link = invoice.PaymentUrl;   // share with the customer
var invoiceDetails = await payriff.Invoices.GetAsync(invoice.InvoiceUuid);
```

## Callbacks

When an order changes state, Payriff POSTs JSON to the `CallbackUrl` you set on the order.

```csharp
app.MapPost("/payriff/callback", async (HttpRequest request, PayriffClient payriff) =>
{
    using var reader = new StreamReader(request.Body);
    var notified = PayriffWebhook.ParseOrderCallback(await reader.ReadToEndAsync());

    // Callbacks are not signed: confirm the state with Payriff before fulfilling.
    var confirmed = await payriff.Orders.GetAsync(notified.OrderId);
    if (confirmed.PaymentStatus == PaymentStatus.Approved)
    {
        // fulfil the order (make this idempotent: the same callback can arrive more than once)
    }
    return Results.Ok();
});
```

## Errors

Every SDK exception extends `PayriffException`, which carries `HttpStatus`, `Code` (Payriff result
code) and `ResponseId` (quote it when contacting support).

| Exception | When |
|---|---|
| `AuthenticationException` | App key rejected (`14010`, `14013`, `14014`, `14015`) |
| `ValidationException` | Invalid request (`15400` or HTTP 400) |
| `RequestRejectedException` | Business refusal (`01000`), e.g. application under review |
| `InsufficientBalanceException` | Not enough wallet balance for a payout (`01200`) |
| `PayoutLimitException` | Payout limit reached (`01300`, `01400`, `01500`) |
| `ApiException` | Any other failure reported by Payriff |
| `PayriffConnectionException` | No response: network error or timeout |

Payriff can report a failure with HTTP 200. The SDK checks the result code in the body, so you
only need to catch exceptions. Invalid arguments throw `ArgumentException` before any request is
sent. Cancelling your own `CancellationToken` throws `OperationCanceledException` as usual.

```csharp
try
{
    await payriff.Orders.RefundAsync(new RefundRequest { OrderId = orderId });
}
catch (ValidationException e)
{
    logger.LogWarning("Refund rejected: {Message} ({Code})", e.Message, e.Code);
}
catch (PayriffConnectionException)
{
    // Outcome unknown: check payriff.Orders.GetAsync(orderId) before retrying
}
catch (PayriffException e)
{
    logger.LogError(e, "Payriff error {Code} responseId={ResponseId}", e.Code, e.ResponseId);
}
```

### Retries

The SDK never retries on its own, because payment calls are not safe to repeat blindly. After a
`PayriffConnectionException`, look the operation up first (`Orders.GetByRequestRrnAsync`,
`Payouts.GetByRequestRrnAsync`) and retry only if it does not exist. Set `RequestRrn` /
`IdempotencyKey` on every request so that this lookup is possible.

## Development

```bash
dotnet test
dotnet pack src/Payriff -c Release
```

## License

MIT