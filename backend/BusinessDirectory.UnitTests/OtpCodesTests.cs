using System.Security.Cryptography;
using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.UnitTests;

public class OtpCodesTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Generated_codes_are_six_digits()
    {
        for (var i = 0; i < 500; i++)
        {
            var code = OtpCodes.Generate();
            Assert.True(OtpCodes.IsWellFormed(code), code);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12 456")]
    [InlineData("12345a")]
    public void Malformed_codes_are_rejected(string? code) => Assert.False(OtpCodes.IsWellFormed(code));

    [Fact]
    public void Correct_code_verifies()
    {
        var hash = OtpCodes.Hash(Key, "+919876543210", "123456");
        Assert.True(OtpCodes.Verify(Key, "+919876543210", "123456", hash));
    }

    [Fact]
    public void Wrong_code_does_not_verify()
    {
        var hash = OtpCodes.Hash(Key, "+919876543210", "123456");
        Assert.False(OtpCodes.Verify(Key, "+919876543210", "123457", hash));
    }

    [Fact]
    public void Code_is_bound_to_the_phone_number()
    {
        var hash = OtpCodes.Hash(Key, "+919876543210", "123456");
        Assert.False(OtpCodes.Verify(Key, "+919876543211", "123456", hash));
    }

    [Fact]
    public void Code_does_not_verify_under_a_different_key()
    {
        var hash = OtpCodes.Hash(Key, "+919876543210", "123456");
        Assert.False(OtpCodes.Verify(RandomNumberGenerator.GetBytes(32), "+919876543210", "123456", hash));
    }

    [Fact]
    public void Stored_hash_does_not_contain_the_code()
    {
        var hash = OtpCodes.Hash(Key, "+919876543210", "123456");
        Assert.Equal(32, hash.Length);
        Assert.DoesNotContain("123456", Convert.ToHexString(hash));
    }
}
