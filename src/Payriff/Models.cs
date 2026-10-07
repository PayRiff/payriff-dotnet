using System.Text.Json.Serialization;

namespace Payriff;

public sealed record Installment(InstallmentProductType Type, InstallmentPeriod Period);

public sealed class Page<T>
{
    public IReadOnlyList<T> Content { get; init; } = [];
    public long TotalElements { get; init; }
    public int TotalPages { get; init; }
    public int Number { get; init; }
    public int Size { get; init; }
    public bool First { get; init; }
    public bool Last { get; init; }
}

public sealed class CardDetails
{
    public string? MaskedPan { get; init; }
    public string? Brand { get; init; }
    public string? Uuid { get; init; }
    public string? CardHolderName { get; init; }
    public string? PhoneNumber { get; init; }
}

public sealed class OrderTransaction
{
    public string? Uuid { get; init; }
    public DateTime? CreatedDate { get; init; }
    public string? Status { get; init; }
    public string? Channel { get; init; }
    public string? ChannelType { get; init; }
    public string? RequestRrn { get; init; }
    public string? ResponseRrn { get; init; }
    public string? ExternalRrn { get; init; }
    public string? Pan { get; init; }
    public string? PaymentWay { get; init; }
    public CardDetails? CardDetails { get; init; }
    public string? CardUuid { get; init; }
    public int? RecurrenceId { get; init; }
    public string? ResponseDescription { get; init; }
    public string? MerchantCategory { get; init; }
    public Installment? Installment { get; init; }
}

public sealed class OrderInfo
{
    public string OrderId { get; init; } = "";
    public string? ExternalTransactionId { get; init; }
    public string? InvoiceUuid { get; init; }
    public decimal? Amount { get; init; }
    [JsonPropertyName("currencyType")] public Currency? Currency { get; init; }
    public string? MerchantName { get; init; }
    public decimal? Commission { get; init; }
    public decimal? CommissionRate { get; init; }
    public decimal? PaidAmount { get; init; }
    public decimal? ExtraPayment { get; init; }
    public Operation? OperationType { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public bool Auto { get; init; }
    public DateTime? CreatedDate { get; init; }
    public string? Description { get; init; }
    public string? Metadata { get; init; }
    public string? IdempotencyKey { get; init; }
    public IReadOnlyList<OrderTransaction> Transactions { get; init; } = [];
}

public sealed class CreateOrderResponse
{
    public string OrderId { get; init; } = "";
    public string? SessionId { get; init; }
    public string? PaymentUrl { get; init; }
    public string? PreviewUrl { get; init; }
    public long? TransactionId { get; init; }
    [JsonPropertyName("comissionRate")] public decimal? CommissionRate { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Fee { get; init; }
    public decimal? TotalAmount { get; init; }
}

public sealed class DirectPayResponse
{
    public string OrderId { get; init; } = "";
    [JsonPropertyName("threeDS")] public bool ThreeDS { get; init; }
    public bool Redirect { get; init; }
    public string? RedirectUrl { get; init; }
    public string? PaymentUrl { get; init; }
    [JsonPropertyName("transactionResponse")] public OrderTransaction? Transaction { get; init; }
}

public sealed class TransactionResponse
{
    public bool Redirect { get; init; }
    public string? RedirectUrl { get; init; }
    [JsonPropertyName("threeDS")] public bool ThreeDS { get; init; }
    public string? AccessUrl { get; init; }
    public string? Channel { get; init; }
    public DirectPayResponse? TransactionResult { get; init; }
}

public sealed class AutoPayResponse
{
    public string OrderId { get; init; } = "";
    public string? PaymentUrl { get; init; }
    public string? Description { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Commission { get; init; }
    public decimal? CommissionRate { get; init; }
    [JsonPropertyName("currencyType")] public Currency? Currency { get; init; }
    public Operation? OperationType { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public bool Auto { get; init; }
    public DateTime? CreatedDate { get; init; }
    public IReadOnlyList<OrderTransaction> Transactions { get; init; } = [];
    [JsonPropertyName("transactionResponseDto")] public TransactionResponse? TransactionResponse { get; init; }
}

public sealed class CardSaveResponse
{
    public string CardSaveId { get; init; } = "";
    public string? OrderId { get; init; }
    public string? SessionId { get; init; }
    public string? PaymentUrl { get; init; }
    public decimal? Amount { get; init; }
    public Currency? Currency { get; init; }
    public CardSaveStatus Status { get; init; }
}

public sealed class CardSaveDetails
{
    public string CardSaveId { get; init; } = "";
    public string? OrderId { get; init; }
    public CardSaveStatus Status { get; init; }
    public string? CardUuid { get; init; }
    public string? MaskedPan { get; init; }
    public string? CardBrand { get; init; }
    public decimal? Amount { get; init; }
    public Currency? Currency { get; init; }
    public string? CustomerRef { get; init; }
    public DateTime? CreatedDate { get; init; }
    public DateTime? VerifiedDate { get; init; }
}

public sealed class SavedCard
{
    public string CardUuid { get; init; } = "";
    public string? MaskedPan { get; init; }
    public string? CardBrand { get; init; }
    public DateTime? CreatedDate { get; init; }
}

public sealed class Transaction
{
    public long? Id { get; init; }
    public long? ApplicationId { get; init; }
    public string? OrderId { get; init; }
    public string? SessionId { get; init; }
    public string? Uuid { get; init; }
    public string? Rrn { get; init; }
    public string? ExternalRrn { get; init; }
    public decimal? Amount { get; init; }
    public decimal? PaidAmount { get; init; }
    public decimal? RefundAmount { get; init; }
    public decimal? RestOfAmount { get; init; }
    public decimal? AmountWithoutFee { get; init; }
    public decimal? PayriffAmount { get; init; }
    public decimal? CommissionRate { get; init; }
    [JsonPropertyName("extra_payment")] public decimal? ExtraPayment { get; init; }
    [JsonPropertyName("currencyType")] public Currency? Currency { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? PaymentSource { get; init; }
    public string? Description { get; init; }
    public string? ResponseDescription { get; init; }
    public Language? OrderLanguage { get; init; }
    public string? TariffType { get; init; }
    public string? Source { get; init; }
    public string? FullName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? BookingId { get; init; }
    public string? InvoiceCode { get; init; }
    public string? Pan { get; init; }
    [JsonPropertyName("card_brand")] public string? CardBrand { get; init; }
    [JsonPropertyName("payment_route")] public string? PaymentRoute { get; init; }
    [JsonPropertyName("payment_way")] public string? PaymentWay { get; init; }
    public string? TransitId { get; init; }
    public string? CreatedDate { get; init; }
    public string? LastModifiedDate { get; init; }
}

public sealed class PayoutResult
{
    [JsonPropertyName("_final")] public string? FinalState { get; init; }
    public string? State { get; init; }
    public string? StateDescription { get; init; }
    public decimal? CurrentDepositBalance { get; init; }
    public long? WalletHistoryId { get; init; }
    public string? BankName { get; init; }
}

public sealed class PayoutStatus
{
    public string? BankName { get; init; }
    public TransferState State { get; init; }
    public string? CardPan { get; init; }
    public decimal? TransferAmount { get; init; }
    public string? CreatedDate { get; init; }
    public string? FormattedDate { get; init; }
    public string? TransferType { get; init; }
    public string? Merchant { get; init; }
    public string? Description { get; init; }
    public string? FullName { get; init; }
    public string? FinCode { get; init; }
}

public sealed class PayoutSummary
{
    public long? Id { get; init; }
    public string? RequestRrn { get; init; }
    public decimal? TransferAmount { get; init; }
    public decimal? AmountWithFee { get; init; }
    public decimal? Fee { get; init; }
    public DateTime? CreatedDate { get; init; }
    public TransferState State { get; init; }
    public string? StateDescription { get; init; }
    public string? CardPan { get; init; }
    public string? FinCode { get; init; }
    public string? FullName { get; init; }
    public string? Description { get; init; }
}

public sealed class Invoice
{
    public long? Id { get; init; }
    public string? MerchantId { get; init; }
    public string? Uuid { get; init; }
    public string InvoiceUuid { get; init; } = "";
    public string? InvoiceCode { get; init; }
    [JsonPropertyName("invoiceStatus")] public InvoiceStatus Status { get; init; }
    public string? PaymentUrl { get; init; }
    public decimal? Amount { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? PayriffAmount { get; init; }
    public decimal? PayriffFee { get; init; }
    public decimal? PayriffFixedFeeAmount { get; init; }
    [JsonPropertyName("currencyType")] public Currency? Currency { get; init; }
    [JsonPropertyName("languageType")] public Language? Language { get; init; }
    public string? PaymentType { get; init; }
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Description { get; init; }
    public string? CustomMessage { get; init; }
    public DateTime? ExpireDate { get; init; }
    public DateTime? CreatedDate { get; init; }
    [JsonPropertyName("approveURL")] public string? ApproveUrl { get; init; }
    [JsonPropertyName("cancelURL")] public string? CancelUrl { get; init; }
    [JsonPropertyName("declineURL")] public string? DeclineUrl { get; init; }
    public bool Active { get; init; }
    public bool SendSms { get; init; }
}

public sealed class InvoiceDetails
{
    public long? Id { get; init; }
    public string? MerchantId { get; init; }
    public string? Uuid { get; init; }
    public string InvoiceUuid { get; init; } = "";
    public string? InvoiceCode { get; init; }
    [JsonPropertyName("invoiceStatus")] public InvoiceStatus Status { get; init; }
    public string? BaseUrl { get; init; }
    public decimal? Amount { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? PayriffAmount { get; init; }
    public decimal? PayriffFee { get; init; }
    public decimal? PayriffFixedFeeAmount { get; init; }
    [JsonPropertyName("currencyType")] public Currency? Currency { get; init; }
    [JsonPropertyName("languageType")] public Language? Language { get; init; }
    public string? PaymentType { get; init; }
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Description { get; init; }
    public DateTime? ExpireDate { get; init; }
    public DateTime? PaymentDay { get; init; }
    public DateTime? ExpireDay { get; init; }
    public DateTime? CreatedDate { get; init; }
    [JsonPropertyName("approveURL")] public string? ApproveUrl { get; init; }
    [JsonPropertyName("cancelURL")] public string? CancelUrl { get; init; }
    [JsonPropertyName("declineURL")] public string? DeclineUrl { get; init; }
    public bool Active { get; init; }
    public int? InstallmentPeriod { get; init; }
    public string? Source { get; init; }
    public bool? DirectPay { get; init; }
    public string? Metadata { get; init; }
}