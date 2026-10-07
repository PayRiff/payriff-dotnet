using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Payriff;

internal sealed class Transport(HttpClient http, string baseUrl, string appKey, string? merchantId, TimeSpan timeout)
{
    private const int MaxErrorBody = 500;
    private static readonly string UserAgent = $"payriff-dotnet/{PayriffClient.Version}";
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public async Task<T?> ExecuteAsync<T>(ApiRequest request, CancellationToken cancellationToken)
    {
        var (status, body) = await SendAsync(request, "application/json", cancellationToken).ConfigureAwait(false);
        var payload = Unwrap(status, body);
        if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
        {
            return default;
        }
        try
        {
            return payload.Value.Deserialize<T>(PayriffJson.Options);
        }
        catch (JsonException e)
        {
            throw new ApiException($"Cannot parse Payriff response: {e.Message}", status, ResultCodes.Success);
        }
    }

    public async Task<byte[]> DownloadAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        var (status, body) = await SendAsync(request, "application/pdf, application/json", cancellationToken).ConfigureAwait(false);
        if (status is >= 200 and < 300)
        {
            return body;
        }
        Unwrap(status, body);
        throw new ApiException("Unexpected response", status);
    }

    private async Task<(int Status, byte[] Body)> SendAsync(ApiRequest request, string accept, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request.Method, Url(request));
        message.Headers.TryAddWithoutValidation("Accept", accept);
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        message.Headers.TryAddWithoutValidation("Authorization", appKey);
        foreach (var (name, value) in request.Headers)
        {
            if (!string.IsNullOrEmpty(value))
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }
        var json = RequestBody(request);
        if (json is not null)
        {
            message.Content = new ByteArrayContent(json);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
            return ((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PayriffConnectionException($"Payriff request timed out after {timeout.TotalSeconds:0.###} s");
        }
        catch (HttpRequestException e)
        {
            throw new PayriffConnectionException($"Payriff request failed: {e.Message}", e);
        }
    }

    private string Url(ApiRequest request)
    {
        var url = new StringBuilder(_baseUrl).Append(request.Path);
        var separator = '?';
        foreach (var (name, value) in request.Query)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }
            url.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }
        return url.ToString();
    }

    private byte[]? RequestBody(ApiRequest request)
    {
        object? body = request.Body;
        if (request.MerchantEnvelope)
        {
            if (string.IsNullOrEmpty(merchantId))
            {
                throw new InvalidOperationException("MerchantId must be configured in PayriffOptions for this operation");
            }
            body = new Dictionary<string, object?> { ["merchant"] = merchantId, ["body"] = body ?? new Dictionary<string, object?>() };
        }
        return body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), PayriffJson.Options);
    }

    private static JsonElement? Unwrap(int status, byte[] body)
    {
        var ok = status is >= 200 and < 300;
        JsonDocument? document = null;
        try
        {
            document = body.Length == 0 ? null : JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
        }
        using (document)
        {
            var root = document?.RootElement;
            if (root is not { ValueKind: JsonValueKind.Object } || !root.Value.TryGetProperty("code", out var codeElement))
            {
                throw new ApiException(ok ? "Unexpected response from Payriff" : Truncate(body, status), status);
            }
            var code = AsString(codeElement);
            if (!ok || code != ResultCodes.Success)
            {
                throw ResultCodes.ToException(Property(root.Value, "message"), status, code, Property(root.Value, "responseId"));
            }
            return root.Value.TryGetProperty("payload", out var payload) ? payload.Clone() : null;
        }
    }

    private static string? Property(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? AsString(value) : null;

    private static string? AsString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        _ => value.GetRawText(),
    };

    private static string Truncate(byte[] body, int status)
    {
        if (body.Length == 0)
        {
            return $"Payriff request failed (HTTP {status})";
        }
        var text = Encoding.UTF8.GetString(body);
        return text.Length > MaxErrorBody ? text[..MaxErrorBody] + "..." : text;
    }
}

internal sealed record ApiRequest(HttpMethod Method, string Path)
{
    public IReadOnlyList<KeyValuePair<string, string?>> Query { get; init; } = [];
    public IReadOnlyDictionary<string, string?> Headers { get; init; } = new Dictionary<string, string?>();
    public object? Body { get; init; }
    public bool MerchantEnvelope { get; init; }
}