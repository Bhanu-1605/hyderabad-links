using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BusinessDirectory.Api.Auth;

/// <summary>One-time-code generation and verification. Only an HMAC of the code is ever stored.</summary>
public static class OtpCodes
{
    /// <summary>6 digits from a cryptographic random number generator.</summary>
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static bool IsWellFormed(string? code) =>
        code is { Length: 6 } && code.All(c => c is >= '0' and <= '9');

    public static byte[] Hash(byte[] key, string phoneE164, string code)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes($"{phoneE164}|{code}"));
    }

    /// <summary>Constant-time comparison, so response timing does not reveal how close a guess was.</summary>
    public static bool Verify(byte[] key, string phoneE164, string code, byte[] expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(key, phoneE164, code), expectedHash);
}
