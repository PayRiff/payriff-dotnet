namespace Payriff;

public enum Currency
{
    Azn = 1,
    Usd,
    Eur,
    Pkr,
    Aed,
    Sar,
}

public enum Language
{
    Az = 1,
    En,
    Ru,
    Ar,
}

public enum Operation
{
    Purchase = 1,
    PreAuth,
    Complete,
    Refund,
    Reverse,
}

public enum AutoPaymentType
{
    None = 1,
    Default,
    Recurring,
}

public enum InstallmentProductType
{
    Birkart = 1,
    Allbali,
    Bolkart,
    Tamkart,
}

public enum InstallmentPeriod
{
    Period = 1,
    Period1, Period2, Period3, Period4, Period5, Period6, Period7, Period8, Period9, Period10, Period11, Period12,
    Period13, Period14, Period15, Period16, Period17, Period18, Period19, Period20, Period21, Period22, Period23, Period24,
}

public enum PaymentStatus
{
    Unknown = 0,
    Created,
    Approved,
    Canceled,
    Declined,
    Refunded,
    PreauthApproved,
    Expired,
    Reverse,
    PartialRefund,
    Partial,
    Accepted,
    RefundInProgress,
    Cash,
    Pending,
    PreauthExpired,
    InReview,
}

public enum CardSaveStatus
{
    Unknown = 0,
    Created,
    Verified,
    Reversed,
    ReverseFailed,
    Declined,
    Expired,
}

public enum TransferState
{
    Unknown = 0,
    Created,
    InProgress,
    NotFound,
    Fail,
    Success,
    DailyPayoutLimitExceeded,
}

public enum InvoiceStatus
{
    Unknown = 0,
    Pending,
    Error,
    Expired,
    Partial,
    Complete,
    Cash,
    Declined,
    Canceled,
}