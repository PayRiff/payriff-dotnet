using System.Security.Cryptography;
using System.Text;

namespace Payriff.Tests;

internal static class CardKeys
{
    public static readonly RSA Private = RSA.Create(2048);

    public static string PublicBase64 => Convert.ToBase64String(Private.ExportSubjectPublicKeyInfo());

    public static string Decrypt(string secretKey, string encryptedMessage)
    {
        var keyAndIv = Private.Decrypt(Convert.FromBase64String(secretKey), RSAEncryptionPadding.OaepSHA256);
        Assert.Equal(44, keyAndIv.Length);
        var data = Convert.FromBase64String(encryptedMessage);
        var plain = new byte[data.Length - 16];
        using var aes = new AesGcm(keyAndIv.AsSpan(0, 32), 16);
        aes.Decrypt(keyAndIv.AsSpan(32, 12), data.AsSpan(0, data.Length - 16), data.AsSpan(data.Length - 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
}