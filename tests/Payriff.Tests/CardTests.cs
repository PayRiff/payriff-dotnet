using System.Security.Cryptography;
using System.Text.Json;

namespace Payriff.Tests;

public sealed class CardTests
{
    private static CardData Card() => new("4169 7413 3015 1979", " JOHN DOE ", "1", "27", "123");

    [Fact]
    public void EncryptedCardDecryptsWithPayriffScheme()
    {
        var (message, secret) = new CardEncryptor(CardKeys.PublicBase64).Encrypt(Card());

        Assert.Equal("""{"pan":"4169741330151979","cardHolder":"JOHN DOE","expiryYear":"2027","expiryMonth":"01","cvv":"123"}""",
            CardKeys.Decrypt(secret, message));
    }

    [Fact]
    public void EveryCallUsesFreshKeyMaterial()
    {
        var encryptor = new CardEncryptor(CardKeys.PublicBase64);

        var a = encryptor.Encrypt(Card());
        var b = encryptor.Encrypt(Card());

        Assert.NotEqual(a.EncryptedMessage, b.EncryptedMessage);
        Assert.NotEqual(a.SecretKey, b.SecretKey);
    }

    [Fact]
    public void ParsesBase64AndPemKeys()
    {
        var key = CardEncryptor.ProductionKey;
        var pem = "-----BEGIN PUBLIC KEY-----\n" + string.Join("\n", key.Chunk(64).Select(c => new string(c))) + "\n-----END PUBLIC KEY-----\n";

        Assert.Equal(CardEncryptor.Parse(key), CardEncryptor.Parse(pem));
    }

    [Fact]
    public void RejectsNonRsaKey()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var error = Assert.Throws<ArgumentException>(() => CardEncryptor.Parse(Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo())));

        Assert.StartsWith("Invalid RSA public key", error.Message);
    }

    [Fact]
    public void NormalizesInput()
    {
        var card = new CardData("4169-7413-3015-1979", "Rəşad", 11, 2027, "1234");
        var (message, secret) = new CardEncryptor(CardKeys.PublicBase64).Encrypt(card);

        Assert.Equal("""{"pan":"4169741330151979","cardHolder":"Rəşad","expiryYear":"2027","expiryMonth":"11","cvv":"1234"}""",
            CardKeys.Decrypt(secret, message));
    }

    [Theory]
    [InlineData("4169", "1", "27", "123", "pan must contain 12-19 digits")]
    [InlineData("4169741330151979000000", "1", "27", "123", "pan must contain 12-19 digits")]
    [InlineData("4169a41330151979", "1", "27", "123", "pan must contain digits only")]
    [InlineData("4169741330151979", "13", "27", "123", "expiryMonth must be 1-12")]
    [InlineData("4169741330151979", "0", "27", "123", "expiryMonth must be 1-12")]
    [InlineData("4169741330151979", "1", "202", "123", "expiryYear must have 2 or 4 digits")]
    [InlineData("4169741330151979", "1", "27", "12", "cvv must contain 3-4 digits")]
    [InlineData("4169741330151979", "1", "27", "", "cvv must contain digits only")]
    [InlineData("٤١٦٩٧٤١٣٣٠١٥١٩٧٩", "1", "27", "123", "pan must contain digits only")]
    public void RejectsInvalidInput(string pan, string month, string year, string cvv, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => new CardData(pan, "JOHN DOE", month, year, cvv));

        Assert.StartsWith(message, error.Message);
    }

    [Fact]
    public void NeverExposesSensitiveData()
    {
        var card = Card();

        var output = string.Join("|", card.ToString(), $"{card}", JsonSerializer.Serialize(card), JsonSerializer.Serialize(new { card }));

        Assert.Contains("416974******1979", output);
        Assert.DoesNotContain("4169741330151979", output);
        Assert.DoesNotContain("123", output);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<CardData>("\"x\""));
    }
}