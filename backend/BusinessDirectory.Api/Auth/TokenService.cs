using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BusinessDirectory.Api.Auth;

/// <summary>
/// Creates short-lived access tokens (JWT) and opaque refresh tokens (stored only as a SHA-256 hash).
/// Owner and admin tokens are signed with the same key but carry different audiences, and each audience
/// is validated by its own authentication scheme, so neither realm's token works on the other's routes.
/// A token carries nothing but the subject id: permissions are read from the database on every request,
/// so removing a role takes effect immediately rather than when the token expires.
/// </summary>
public sealed class TokenService(AuthSettings settings)
{
    public TokenValidationParameters ValidationParameters => ParametersFor(settings.Audience);

    public TokenValidationParameters AdminValidationParameters => ParametersFor(AuthSettings.AdminAudience);

    public (string Token, int ExpiresInSeconds) CreateAccessToken(long userId) =>
        Create(userId, settings.Audience, settings.AccessTokenMinutes);

    public (string Token, int ExpiresInSeconds) CreateAdminAccessToken(long adminUserId) =>
        Create(adminUserId, AuthSettings.AdminAudience, settings.AdminAccessTokenMinutes);

    private TokenValidationParameters ParametersFor(string audience) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(settings.JwtKey),
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };

    private (string Token, int ExpiresInSeconds) Create(long subjectId, string audience, int minutes)
    {
        var lifetime = TimeSpan.FromMinutes(minutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, subjectId.ToString(System.Globalization.CultureInfo.InvariantCulture)) }),
            Issuer = settings.Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.Add(lifetime),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(settings.JwtKey), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }

    public static string NewRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] HashRefreshToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
