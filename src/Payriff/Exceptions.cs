namespace Payriff;

public class PayriffException : Exception
{
    public PayriffException(string message, int httpStatus = 0, string? code = null, string? responseId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatus = httpStatus;
        Code = code;
        ResponseId = responseId;
    }

    public int HttpStatus { get; }

    public string? Code { get; }

    public string? ResponseId { get; }
}

public class ApiException(string message, int httpStatus, string? code = null, string? responseId = null)
    : PayriffException(message, httpStatus, code, responseId);

public sealed class AuthenticationException(string message, int httpStatus, string? code, string? responseId)
    : ApiException(message, httpStatus, code, responseId);

public sealed class ValidationException(string message, int httpStatus, string? code, string? responseId)
    : ApiException(message, httpStatus, code, responseId);

public sealed class RequestRejectedException(string message, int httpStatus, string? code, string? responseId)
    : ApiException(message, httpStatus, code, responseId);

public sealed class InsufficientBalanceException(string message, int httpStatus, string? code, string? responseId)
    : ApiException(message, httpStatus, code, responseId);

public sealed class PayoutLimitException(string message, int httpStatus, string? code, string? responseId)
    : ApiException(message, httpStatus, code, responseId);

public sealed class PayriffConnectionException(string message, Exception? innerException = null)
    : PayriffException(message, 0, null, null, innerException);

internal static class ResultCodes
{
    public const string Success = "00000";

    public static ApiException ToException(string? message, int httpStatus, string? code, string? responseId)
    {
        var msg = string.IsNullOrEmpty(message) ? $"Payriff request failed (HTTP {httpStatus})" : message;
        return code switch
        {
            "14010" or "14013" or "14014" or "14015" => new AuthenticationException(msg, httpStatus, code, responseId),
            "01300" or "01400" or "01500" => new PayoutLimitException(msg, httpStatus, code, responseId),
            "01200" => new InsufficientBalanceException(msg, httpStatus, code, responseId),
            "01000" => new RequestRejectedException(msg, httpStatus, code, responseId),
            "15400" => new ValidationException(msg, httpStatus, code, responseId),
            _ => httpStatus switch
            {
                401 or 403 => new AuthenticationException(msg, httpStatus, code, responseId),
                400 => new ValidationException(msg, httpStatus, code, responseId),
                _ => new ApiException(msg, httpStatus, code, responseId),
            },
        };
    }
}