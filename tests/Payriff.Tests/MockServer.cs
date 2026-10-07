using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Payriff.Tests;

public sealed record RecordedRequest(string Method, string Url, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public JsonNode Json => JsonNode.Parse(Body)!;

    public string? Header(string name) => Headers.TryGetValue(name.ToLowerInvariant(), out var v) ? v : null;
}

public sealed class MockServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, (int Status, Dictionary<string, string> Headers, byte[] Body)> _stubs = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public MockServer()
    {
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public string Url { get; }

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    public RecordedRequest Last => _requests.Last();

    public void Stub(string method, string url, int status = 200, Dictionary<string, string>? headers = null, object? body = null)
    {
        var raw = body switch
        {
            null => [],
            byte[] b => b,
            string s => Encoding.UTF8.GetBytes(s),
            _ => JsonSerializer.SerializeToUtf8Bytes(body),
        };
        _stubs[$"{method} {url}"] = (status, headers ?? new Dictionary<string, string>(), raw);
    }

    public void Ok(string method, string url, object? payload)
        => Stub(method, url, body: new Dictionary<string, object?> { ["code"] = "00000", ["message"] = "Operation performed successfully", ["payload"] = payload });

    public void OkRaw(string method, string url, string payloadJson)
        => Stub(method, url, body: $"{{\"code\":\"00000\",\"message\":\"Operation performed successfully\",\"payload\":{payloadJson}}}");

    public PayriffClient Client(string? merchantId = "ES1000000", string? cardEncryptionKey = null)
        => new(new PayriffOptions
        {
            AppKey = "app-key",
            MerchantId = merchantId,
            BaseUrl = new Uri(Url),
            CardEncryptionKey = cardEncryptionKey,
        });

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            using var ms = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(ms);
            var headers = context.Request.Headers.AllKeys.Where(k => k is not null)
                .ToDictionary(k => k!.ToLowerInvariant(), k => context.Request.Headers[k]!);
            var target = context.Request.RawUrl ?? "/";
            _requests.Enqueue(new RecordedRequest(context.Request.HttpMethod, target, headers, ms.ToArray()));
            var key = $"{context.Request.HttpMethod} {target}";
            if (!_stubs.TryGetValue(key, out var stub) && !_stubs.TryGetValue($"{context.Request.HttpMethod} {target.Split('?')[0]}", out stub))
            {
                stub = (404, new Dictionary<string, string>(), []);
            }
            context.Response.StatusCode = stub.Status;
            context.Response.ContentType = "application/json";
            foreach (var (name, value) in stub.Headers)
            {
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.ContentType = value;
                }
                else
                {
                    context.Response.Headers[name] = value;
                }
            }
            context.Response.ContentLength64 = stub.Body.Length;
            await context.Response.OutputStream.WriteAsync(stub.Body);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}