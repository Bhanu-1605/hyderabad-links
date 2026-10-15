using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.UnitTests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("9876543210")]
    [InlineData("09876543210")]
    [InlineData("919876543210")]
    [InlineData("+919876543210")]
    [InlineData("+91 98765 43210")]
    [InlineData("+91-98765-43210")]
    [InlineData("  98765 43210 ")]
    public void Accepts_common_indian_mobile_formats(string input)
    {
        Assert.True(PhoneNumber.TryNormalizeIndia(input, out var e164));
        Assert.Equal("+919876543210", e164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("5876543210")]          // Indian mobiles start with 6-9
    [InlineData("98765432101")]         // too long
    [InlineData("+14155552671")]        // other country
    [InlineData("98765abcde")]
    [InlineData("98765+43210")]         // plus sign only allowed at the start
    [InlineData("0000000000000000000000000000")]
    public void Rejects_everything_else(string? input)
    {
        Assert.False(PhoneNumber.TryNormalizeIndia(input, out var e164));
        Assert.Equal("", e164);
    }
}
