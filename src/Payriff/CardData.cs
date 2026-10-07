using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Payriff;

[JsonConverter(typeof(MaskedCardConverter))]
public sealed partial class CardData
{
    private readonly string _pan;
    private readonly string _cardHolder;
    private readonly string _expiryYear;
    private readonly string _expiryMonth;
    private readonly string _cvv;

    public CardData(string pan, string cardHolder, string expiryMonth, string expiryYear, string cvv)
    {
        _pan = Digits(pan, nameof(pan));
        if (_pan.Length is < 12 or > 19)
        {
            throw new ArgumentException("pan must contain 12-19 digits", nameof(pan));
        }
        ArgumentNullException.ThrowIfNull(cardHolder);
        var month = int.Parse(Digits(expiryMonth, nameof(expiryMonth)), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12)
        {
            throw new ArgumentException("expiryMonth must be 1-12", nameof(expiryMonth));
        }
        var year = Digits(expiryYear, nameof(expiryYear));
        year = year.Length switch
        {
            2 => "20" + year,
            4 => year,
            _ => throw new ArgumentException("expiryYear must have 2 or 4 digits", nameof(expiryYear)),
        };
        _cvv = Digits(cvv, nameof(cvv));
        if (_cvv.Length is < 3 or > 4)
        {
            throw new ArgumentException("cvv must contain 3-4 digits", nameof(cvv));
        }
        _cardHolder = cardHolder.Trim();
        _expiryMonth = month.ToString("00", CultureInfo.InvariantCulture);
        _expiryYear = year;
    }

    public CardData(string pan, string cardHolder, int expiryMonth, int expiryYear, string cvv)
        : this(pan, cardHolder, expiryMonth.ToString(CultureInfo.InvariantCulture), expiryYear.ToString(CultureInfo.InvariantCulture), cvv)
    {
    }

    public override string ToString() => $"CardData {{ {_pan[..6]}******{_pan[^4..]}, {_expiryMonth}/{_expiryYear} }}";

    internal byte[] ToPayloadJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("pan", _pan);
            writer.WriteString("cardHolder", _cardHolder);
            writer.WriteString("expiryYear", _expiryYear);
            writer.WriteString("expiryMonth", _expiryMonth);
            writer.WriteString("cvv", _cvv);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    [GeneratedRegex(@"[\s-]")]
    private static partial Regex Separators();

    private static string Digits(string value, string field)
    {
        ArgumentNullException.ThrowIfNull(value, field);
        var v = Separators().Replace(value, "");
        if (v.Length == 0 || !v.All(char.IsAsciiDigit))
        {
            throw new ArgumentException($"{field} must contain digits only", field);
        }
        return v;
    }

    private sealed class MaskedCardConverter : JsonConverter<CardData>
    {
        public override CardData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("CardData cannot be deserialized");

        public override void Write(Utf8JsonWriter writer, CardData value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}

internal sealed class CardEncryptor
{
    public const string ProductionKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAxRq5a+44T6Dac60XmVRQ/7cpPyFsBnamXbJlRVJk8CnES5Re5tVMohyD0hZr" +
        "3zcQj+bxodYB4zZpQTPlrXvFBC3zz+rXnGlevxBQ6W2d3QC9q8vWH8p3ZOwTO3qVvDSHH9o+hMMRNbJ7kueq/KZlX/F+bjZ23CZw7iXE" +
        "GQT3HYVYnnHsvpaguYDteWBag2sPPLLsVjeB3zhTfQ7OsWp5XTkDuRwLugHPvs6RHLcwGCnodukWyvwUaEUQR/kMGC+RbMsAIVkcLMP5" +
        "csfR3Xo7Gi98+i44iLN00f7gE8QvEmvv8xDspyTAjDEL1a5gK7TijJ3yLG/Bwa1rr1uskYy7lwIDAQAB";

    private const int AesKeyBytes = 32;
    private const int IvBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _spki;

    public CardEncryptor(string base64OrPem)
    {
        _spki = Parse(base64OrPem);
    }

    public static byte[] Parse(string base64OrPem)
    {
        ArgumentNullException.ThrowIfNull(base64OrPem);
        var body = new StringBuilder(base64OrPem)
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .ToString();
        body = string.Concat(body.Where(c => !char.IsWhiteSpace(c)));
        try
        {
            var der = Convert.FromBase64String(body);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(der, out var read);
            if (read != der.Length)
            {
                throw new CryptographicException("trailing data");
            }
            return der;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            throw new ArgumentException("Invalid RSA public key", nameof(base64OrPem), e);
        }
    }

    public (string EncryptedMessage, string SecretKey) Encrypt(CardData card)
    {
        var keyAndIv = RandomNumberGenerator.GetBytes(AesKeyBytes + IvBytes);
        var plaintext = card.ToPayloadJson();
        var ciphertext = new byte[plaintext.Length + TagBytes];
        try
        {
            using (var aes = new AesGcm(keyAndIv.AsSpan(0, AesKeyBytes), TagBytes))
            {
                aes.Encrypt(keyAndIv.AsSpan(AesKeyBytes, IvBytes), plaintext,
                    ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length));
            }
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(_spki, out _);
            var secret = rsa.Encrypt(keyAndIv, RSAEncryptionPadding.OaepSHA256);
            return (Convert.ToBase64String(ciphertext), Convert.ToBase64String(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyAndIv);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}