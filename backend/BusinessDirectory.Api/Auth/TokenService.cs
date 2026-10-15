using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BusinessDirectory.Api.Auth;

/// <summary>Creates short-lived owner access tokens (JWT) and opaque refresh tokens (stored only as a SHA-256 hash).</summary>
public sealed class TokenService(AuthSettings settings)
{
    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = settings.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(settings.JwtKey),
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };

    public (string Token, int ExpiresInSeconds) CreateAccessToken(long userId)
    {
        var lifetime = TimeSpan.FromMinutes(settings.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)) }),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Expires = DateTime.UtcNow.Add(lifetime),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(settings.JwtKey), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }

    public static string NewRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] HashRefreshToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
