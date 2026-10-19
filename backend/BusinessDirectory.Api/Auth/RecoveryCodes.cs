using System.Security.Cryptography;
using System.Text;

namespace BusinessDirectory.Api.Auth;

/// <summary>
/// The one-time codes an administrator uses when they have lost their authenticator.
///
/// Only a hash is ever stored, like the refresh tokens. Unlike a password these are high-entropy
/// random strings, so a single SHA-256 is enough: there is nothing to guess and nothing to brute
/// force, and a slow hash here would only make sign-in slower.
/// </summary>
public static class RecoveryCodes
{
    public const int CodeCount = 10;

    /// <summary>Five bytes per group, printed as eight Base32 characters. Two groups = 50 bits.</summary>
    private const int GroupBytes = 5;

    /// <summary>
    /// Generates a fresh set. Returned in the clear exactly once, for the admin to write down; after
    /// that only <see cref="Hash"/> of each survives.
    /// </summary>
    public static IReadOnlyList<string> Generate() =>
        Enumerable.Range(0, CodeCount).Select(_ => NewCode()).ToList();

    private static string NewCode()
    {
        var left = Base32.Encode(RandomNumberGenerator.GetBytes(GroupBytes));
        var right = Base32.Encode(RandomNumberGenerator.GetBytes(GroupBytes));
        return $"{left}-{right}";
    }

    /// <summary>
    /// Accepts what the admin typed however they typed it: spaces, lowercase and missing hyphens are
    /// all normalised away before hashing, so the stored hash matches regardless.
    /// </summary>
    public static string Normalize(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var character in code)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return builder.ToString();
    }

    public static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)));
}
