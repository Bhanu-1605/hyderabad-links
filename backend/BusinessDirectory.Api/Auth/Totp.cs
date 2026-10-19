using System.Security.Cryptography;
using System.Text;

namespace BusinessDirectory.Api.Auth;

/// <summary>
/// Time-based one-time passwords (RFC 6238), as authenticator apps implement them.
///
/// Written out rather than taken from a package: the algorithm is HMAC-SHA1 over a counter plus the
/// dynamic truncation below, and the codebase already hand-writes its password hashing rather than
/// using ASP.NET Core Identity (decisions D7 and D17). SHA-1 is not a free choice here — it is what
/// every authenticator app implements, and HMAC-SHA1 is not affected by SHA-1's collision weakness.
/// </summary>
public static class Totp
{
    /// <summary>Every authenticator app assumes 30-second steps and 6 digits.</summary>
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>
    /// How many steps either side of now are accepted, for clock drift between the server and the
    /// phone. One step each way means a code stays valid for at most 90 seconds.
    /// </summary>
    public const int WindowSteps = 1;

    /// <summary>160 bits, the size RFC 4226 recommends for an HMAC-SHA1 key.</summary>
    public const int SecretBytes = 20;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    /// <summary>The step number a moment falls in. Stored after a successful check so it cannot be replayed.</summary>
    public static long StepAt(DateTimeOffset moment) => moment.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>
    /// Checks a code against the steps around <paramref name="now"/>, and reports which step matched
    /// so the caller can refuse that same step a second time.
    /// </summary>
    /// <param name="lastUsedStep">
    /// The step a previous code was accepted at, or null. Anything at or before it is refused even if
    /// the arithmetic matches: within the window the same code would otherwise work twice.
    /// </param>
    public static bool TryVerify(byte[] secret, string? code, DateTimeOffset now, long? lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (secret.Length == 0 || code is null || code.Length != Digits)
        {
            return false;
        }

        foreach (var character in code)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        var current = StepAt(now);
        for (var offset = -WindowSteps; offset <= WindowSteps; offset++)
        {
            var step = current + offset;
            if (lastUsedStep is { } used && step <= used)
            {
                continue;   // already spent, or older than one that was
            }

            // Fixed-time comparison: a wrong code must not be distinguishable by how long it took.
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Compute(secret, step)),
                    Encoding.ASCII.GetBytes(code)))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    /// <summary>The code for one step. Public so the tests can generate what an authenticator app would.</summary>
    public static string Compute(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> mac = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, mac);

        // Dynamic truncation (RFC 4226 section 5.3): the low nibble of the last byte picks where to read.
        var offset = mac[^1] & 0x0F;
        var binary = ((mac[offset] & 0x7F) << 24)
                   | (mac[offset + 1] << 16)
                   | (mac[offset + 2] << 8)
                   | mac[offset + 3];

        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The otpauth:// URI an authenticator app reads from a QR code. The secret is in it, so this
    /// string is as sensitive as the secret: it goes to the admin enrolling, and nowhere else.
    /// </summary>
    public static string EnrollmentUri(string issuer, string accountEmail, byte[] secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountEmail}");
        var parameters = string.Join('&',
            $"secret={Base32.Encode(secret)}",
            $"issuer={Uri.EscapeDataString(issuer)}",
            "algorithm=SHA1",
            $"digits={Digits}",
            $"period={StepSeconds}");
        return $"otpauth://totp/{label}?{parameters}";
    }
}

/// <summary>
/// Base32 (RFC 4648) without padding, which is how authenticator apps expect a TOTP secret to be
/// written. The BCL has no Base32, and this is the only place that needs it.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder();
        var buffer = 0;
        var bitsInBuffer = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                builder.Append(Alphabet[(buffer >> bitsInBuffer) & 31]);
            }
        }

        if (bitsInBuffer > 0)
        {
            // Pad the final group on the right with zero bits.
            builder.Append(Alphabet[(buffer << (5 - bitsInBuffer)) & 31]);
        }

        return builder.ToString();
    }
}
