namespace Payriff;

public sealed class CreateOrderRequest
{
    public required decimal Amount { get; init; }
    public Currency Currency { get; init; } = Currency.Azn;
    public Language? Language { get; init; }
    public Operation Operation { get; init; } = Operation.Purchase;
    public string? Description { get; init; }
    public string? CallbackUrl { get; init; }
    public string? RedirectUrl { get; init; }
    public bool? CardSave { get; init; }
    public bool? ThreeDS { get; init; }
    public AutoPaymentType? AutoPaymentType { get; init; }
    public Installment? Installment { get; init; }
    public string? FullName { get; init; }
    public string? PhoneNumber { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public IReadOnlyDictionary<string, string>? Fields { get; init; }
    public string? RequestRrn { get; init; }
}

public sealed class RefundRequest
{
    public required string OrderId { get; init; }
    public decimal? Amount { get; init; }
    public string? RefundReason { get; init; }
    public string? CallbackUrl { get; init; }
}

public sealed class CompleteRequest
{
    public required string OrderId { get; init; }
    public decimal? Amount { get; init; }
    public string? CallbackUrl { get; init; }
}

public sealed class DirectPayRequest
{
    public required decimal Amount { get; init; }
    public required string Description { get; init; }
    public required CardData Card { get; init; }
    public Operation Operation { get; init; } = Operation.Purchase;
    public Currency Currency { get; init; } = Currency.Azn;
    public string? CallbackUrl { get; init; }
    public bool? ThreeDS { get; init; }
    public IReadOnlyDictionary<string, string>? CustomFields { get; init; }
    public bool CardSave { get; init; }
    public string? RequestRrn { get; init; }
}

public sealed class AutoPayRequest
{
    public required string CardUuid { get; init; }
    public required decimal Amount { get; init; }
    public required string Description { get; init; }
    public Operation Operation { get; init; } = Operation.Purchase;
    public Currency Currency { get; init; } = Currency.Azn;
    public string? CallbackUrl { get; init; }
    public bool? ThreeDS { get; init; }
    public bool? OneClickPayment { get; init; }
    public string? RequestRrn { get; init; }
}

public sealed class CardSaveRequest
{
    public required string CustomerRef { get; init; }
    public required string CallbackUrl { get; init; }
    public string? Description { get; init; }
    public Language? Language { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed class PayoutRequest
{
    public required decimal TransferAmount { get; init; }
    public required string Description { get; init; }
    public required string FullName { get; init; }
    public required string FinCode { get; init; }
    public string? CardPan { get; init; }
    public string? BankName { get; init; }
    public string? CardType { get; init; }
    public string? RequestRrn { get; init; }
    public string? CustomerCode { get; init; }
    public string? Voen { get; init; }
    public string? BirthDate { get; init; }
    public string? CallbackUrl { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed class InvoiceCreateRequest
{
    public decimal? Amount { get; init; }
    public bool? AmountDynamic { get; init; }
    public Currency Currency { get; init; } = Currency.Azn;
    public Language? Language { get; init; }
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Description { get; init; }
    public string? CustomMessage { get; init; }
    public DateTime? ExpireDate { get; init; }
    public string? ApproveUrl { get; init; }
    public string? CancelUrl { get; init; }
    public string? DeclineUrl { get; init; }
    public string? RedirectUrl { get; init; }
    public InstallmentProductType? InstallmentProductType { get; init; }
    public int? InstallmentPeriod { get; init; }
    public bool? DirectPay { get; init; }
    public bool? SendSms { get; init; }
    public bool? SendWhatsapp { get; init; }
    public bool? SendEmail { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public string? ExternalTransactionId { get; init; }
}

public sealed class TransactionFilter
{
    public string? OrderId { get; init; }
    public string? Rrn { get; init; }
    public PaymentStatus? Status { get; init; }
    public string? Amount { get; init; }
    public string? Description { get; init; }
    public string? Name { get; init; }
    public string? FullName { get; init; }
    public string? CardNumber { get; init; }
    public string? BookingId { get; init; }
    public string? InvoiceCode { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; }
    public int Size { get; init; } = 10;
}

public sealed class PayoutFilter
{
    public string? Rrn { get; init; }
    public TransferState? Status { get; init; }
    public string? Amount { get; init; }
    public string? Description { get; init; }
    public string? FullName { get; init; }
    public string? FinCode { get; init; }
    public string? BankSource { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; }
    public int Size { get; init; } = 10;
}