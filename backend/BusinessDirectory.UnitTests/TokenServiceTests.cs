using System.Security.Cryptography;
using BusinessDirectory.Api.Auth;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BusinessDirectory.UnitTests;

public class TokenServiceTests
{
    private static AuthSettings NewSettings(string audience = AuthSettings.OwnerAudience) => new()
    {
        JwtKey = RandomNumberGenerator.GetBytes(32),
        OtpKey = RandomNumberGenerator.GetBytes(32),
        Audience = audience,
    };

    [Fact]
    public async Task Access_token_round_trips_and_carries_the_user_id()
    {
        var service = new TokenService(NewSettings());
        var (token, expiresIn) = service.CreateAccessToken(42);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, service.ValidationParameters);

        Assert.True(result.IsValid);
        Assert.Equal("42", result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(15 * 60, expiresIn);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = new TokenService(NewSettings()).CreateAccessToken(42).Token;
        var other = new TokenService(NewSettings());

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, other.ValidationParameters);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        var settings = NewSettings();
        var adminLike = new TokenService(new AuthSettings { JwtKey = settings.JwtKey, OtpKey = settings.OtpKey, Audience = "admin" });
        var token = adminLike.CreateAccessToken(42).Token;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenService(settings).ValidationParameters);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Refresh_tokens_are_unique_and_long()
    {
        var a = TokenService.NewRefreshToken();
        var b = TokenService.NewRefreshToken();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 43);
    }

    [Fact]
    public void Refresh_token_hash_is_stable_and_is_not_the_token()
    {
        var token = TokenService.NewRefreshToken();
        Assert.Equal(TokenService.HashRefreshToken(token), TokenService.HashRefreshToken(token));
        Assert.NotEqual(TokenService.HashRefreshToken(token), TokenService.HashRefreshToken(token + "x"));
        Assert.Equal(32, TokenService.HashRefreshToken(token).Length);
    }
}
