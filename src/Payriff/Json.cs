using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payriff;

internal static class PayriffJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new WireEnumConverterFactory(), new LenientDateTimeConverter(), new LenientNullableDateTimeConverter() },
    };

    private static readonly ConcurrentDictionary<Enum, string> WireNames = new();

    public static string ToWire(Enum value) => WireNames.GetOrAdd(value, static v =>
    {
        var name = v.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0)
            {
                var prev = name[i - 1];
                if ((char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev))) || (char.IsDigit(c) && char.IsLetter(prev)))
                {
                    sb.Append('_');
                }
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    });

    public static bool TryFromWire(Type enumType, string? wire, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(wire))
        {
            return false;
        }
        var compact = wire.Replace("_", string.Empty, StringComparison.Ordinal);
        if (compact.Length == 0 || char.IsDigit(compact[0]) || compact[0] == '-')
        {
            return false;
        }
        return Enum.TryParse(enumType, compact, ignoreCase: true, out value) && Enum.IsDefined(enumType, value!);
    }
}

internal sealed class WireEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var type = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        return type.IsEnum && type.Namespace == typeof(Currency).Namespace;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert);
        var converterType = underlying is null
            ? typeof(WireEnumConverter<>).MakeGenericType(typeToConvert)
            : typeof(NullableWireEnumConverter<>).MakeGenericType(underlying);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

internal sealed class WireEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly T Fallback = Enum.TryParse<T>("Unknown", out var unknown) ? unknown : default;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
        }
        return PayriffJson.TryFromWire(typeof(T), text, out var value) ? (T)value! : Fallback;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(PayriffJson.ToWire(value));
}

internal sealed class NullableWireEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
{
    private static readonly bool HasUnknown = Enum.IsDefined(typeof(T), "Unknown");

    public override bool HandleNull => true;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
        }
        if (PayriffJson.TryFromWire(typeof(T), text, out var value))
        {
            return (T)value!;
        }
        return HasUnknown ? Enum.Parse<T>("Unknown") : null;
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(PayriffJson.ToWire(value.Value));
        }
    }
}

internal sealed class LenientNullableDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd",
    ];

    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }
        var text = reader.GetString();
        return DateTime.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }
    }
}

internal sealed class LenientDateTimeConverter : JsonConverter<DateTime>
{
    private static readonly LenientNullableDateTimeConverter Inner = new();

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Inner.Read(ref reader, typeof(DateTime?), options) ?? default;

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => Inner.Write(writer, value, options);
}