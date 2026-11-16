using System.Security.Cryptography;
using System.Text;

namespace HorizonControl.Api.Security;

public static class SecretHasher
{
    public static string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes);
    }

    public static bool FixedTimeEqualsHash(string plaintext, string expectedHexHash)
    {
        var actual = Hash(plaintext);
        var a = Encoding.UTF8.GetBytes(actual);
        var b = Encoding.UTF8.GetBytes(expectedHexHash);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string CreateAgentSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
