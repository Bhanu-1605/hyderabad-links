using System.Security.Cryptography;
using System.Text;

namespace BusinessDirectory.Api.Auth;

/// <summary>
/// Admin password hashing. PBKDF2-HMAC-SHA256 from the .NET base library, so there is no extra
/// dependency to keep up to date. The stored string carries everything needed to verify it later:
///
///     pbkdf2-sha256$&lt;iterations&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;
///
/// Keeping the iteration count in the string means it can be raised for new passwords without
/// invalidating existing ones.
/// </summary>
public static class AdminPasswords
{
    public const int MinimumLength = 12;

    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 210_000;   // OWASP's 2023 guidance for PBKDF2-HMAC-SHA256
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, Iterations);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Returns false for an unusable stored value (null, malformed, or an account that has no password yet)
    /// rather than throwing, so a damaged row cannot turn a failed login into a 500.
    /// </summary>
    public static bool Verify(string? password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations) || iterations < 1000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Spends the same time as a real check. Sign-in calls this when the email is unknown, so an attacker
    /// cannot tell a real admin address from a made-up one by timing the response.
    /// </summary>
    public static void VerifyAgainstDecoy(string? password) => Verify(password, Decoy.Value);

    private static readonly Lazy<string> Decoy = new(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    /// <summary>The rule a new admin password must meet. Length does the work; complexity rules push people to patterns.</summary>
    public static bool IsStrongEnough(string? password) =>
        password is not null && password.Length >= MinimumLength && password.Trim().Length >= MinimumLength;

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, length);
}
