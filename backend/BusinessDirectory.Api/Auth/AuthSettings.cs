using System.Security.Cryptography;

namespace BusinessDirectory.Api.Auth;

/// <summary>
/// Login settings for owners and admins. Secrets come from configuration (Key Vault / environment variables in Azure), never from source code.
/// In Development, missing keys are replaced by random ones generated at startup (sessions then end on every restart).
/// Outside Development, missing or weak keys stop the app from starting.
/// </summary>
public sealed class AuthSettings
{
    public const string OwnerAudience = "owner";

    /// <summary>
    /// Admin tokens carry their own audience, so an owner token presented on an /api/v1/admin route
    /// fails validation outright instead of relying on a permission check further in.
    /// (The super-admin surface gets "superadmin" when it is built; see docs/security.md.)
    /// </summary>
    public const string AdminAudience = "admin";

    public required byte[] JwtKey { get; init; }
    public required byte[] OtpKey { get; init; }
    public string Issuer { get; init; } = "hyderabadlinks-api";
    public string Audience { get; init; } = OwnerAudience;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
    public int OtpTtlSeconds { get; init; } = 300;
    public int ResendCooldownSeconds { get; init; } = 30;
    public int MaxOtpPerPhonePerHour { get; init; } = 5;
    public int MaxOtpPerIpPerHour { get; init; } = 20;

    // Admin sign-in. Shorter-lived than an owner session: admins work in sittings, owners come back weeks later.
    public int AdminAccessTokenMinutes { get; init; } = 15;
    public int AdminRefreshTokenDays { get; init; } = 7;

    /// <summary>Wrong passwords allowed before the account is locked for <see cref="AdminLockoutMinutes"/>.</summary>
    public int AdminMaxFailedLogins { get; init; } = 5;
    public int AdminLockoutMinutes { get; init; } = 15;

    public static AuthSettings Load(IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection("Auth");
        return new AuthSettings
        {
            JwtKey = ReadKey(section["JwtSigningKey"], "Auth:JwtSigningKey", isDevelopment),
            OtpKey = ReadKey(section["OtpHmacKey"], "Auth:OtpHmacKey", isDevelopment),
            Issuer = section["Issuer"] ?? "hyderabadlinks-api",
            AccessTokenMinutes = section.GetValue("AccessTokenMinutes", 15),
            RefreshTokenDays = section.GetValue("RefreshTokenDays", 30),
            OtpTtlSeconds = section.GetValue("OtpTtlSeconds", 300),
            ResendCooldownSeconds = section.GetValue("ResendCooldownSeconds", 30),
            MaxOtpPerPhonePerHour = section.GetValue("MaxOtpPerPhonePerHour", 5),
            MaxOtpPerIpPerHour = section.GetValue("MaxOtpPerIpPerHour", 20),
            AdminAccessTokenMinutes = section.GetValue("AdminAccessTokenMinutes", 15),
            AdminRefreshTokenDays = section.GetValue("AdminRefreshTokenDays", 7),
            AdminMaxFailedLogins = section.GetValue("AdminMaxFailedLogins", 5),
            AdminLockoutMinutes = section.GetValue("AdminLockoutMinutes", 15),
        };
    }

    private static byte[] ReadKey(string? base64, string name, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            if (isDevelopment)
            {
                return RandomNumberGenerator.GetBytes(32);
            }
            throw new InvalidOperationException($"{name} must be set (base64, at least 32 bytes) outside Development.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{name} must be a base64 string.");
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException($"{name} must decode to at least 32 bytes.");
        }
        return key;
    }
}
