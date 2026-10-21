using BusinessDirectory.Api.Notifications;

namespace BusinessDirectory.UnitTests;

// The addresses this sees come from our own database, so the question is not "is this RFC-valid"
// but "is handing this to a provider worth the attempt, and is it safe to". The newline cases are
// the ones that matter: a stored address with a line break in it must never reach a provider's API.
public class EmailAddressTests
{
    [Theory]
    [InlineData("owner@example.com")]
    [InlineData("ravi.kumar@shop.co.in")]
    [InlineData("a+tag@example.org")]
    public void A_usable_address_is_accepted(string candidate) =>
        Assert.True(EmailAddress.IsValid(candidate));

    [Fact]
    public void It_is_trimmed_and_lowercased_to_match_the_column()
    {
        Assert.True(EmailAddress.TryNormalize("  Owner@Example.COM  ", out var normalized));

        // The notifications and enquiries columns both require lowercase, so normalising here is
        // what keeps the insert from being refused.
        Assert.Equal("owner@example.com", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("@example.com")]
    [InlineData("owner@")]
    [InlineData("owner@example")]
    [InlineData("owner@example.c")]
    [InlineData("two@@example.com")]
    [InlineData("owner name@example.com")]
    public void Anything_we_would_not_send_to_is_refused(string? candidate) =>
        Assert.False(EmailAddress.IsValid(candidate));

    [Theory]
    [InlineData("owner@example.com\nbcc: someone@else.com")]
    [InlineData("owner@example.com\r\nSubject: injected")]
    [InlineData("owner@example.com\tbcc")]
    public void An_address_carrying_an_extra_header_is_refused(string candidate)
    {
        Assert.False(EmailAddress.TryNormalize(candidate, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void An_address_longer_than_the_column_is_refused()
    {
        var tooLong = new string('a', EmailAddress.MaxLength) + "@example.com";

        Assert.False(EmailAddress.IsValid(tooLong));
    }

    [Fact]
    public void An_address_at_exactly_the_limit_is_accepted()
    {
        var local = new string('a', EmailAddress.MaxLength - "@example.com".Length);

        Assert.True(EmailAddress.IsValid(local + "@example.com"));
    }

    [Fact]
    public void A_failed_normalisation_leaves_nothing_behind()
    {
        Assert.False(EmailAddress.TryNormalize("rubbish", out var normalized));
        Assert.Equal(string.Empty, normalized);
    }
}
