using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.UnitTests;

public class ProfileValidatorTests
{
    private static ProfileValidationResult Validate(string? name, string? email) =>
        ProfileValidator.Validate(new ProfileInput(name, email));

    [Fact]
    public void A_complete_profile_is_accepted_and_trimmed()
    {
        var result = Validate("  Ramesh Kumar  ", "  ramesh@example.com  ");

        Assert.Empty(result.Errors);
        Assert.Equal("Ramesh Kumar", result.Value!.Name);
        Assert.Equal("ramesh@example.com", result.Value.Email);
    }

    [Theory]
    [InlineData("Ravi")]
    [InlineData("D'Souza")]
    [InlineData("Dr. A. P. Rao")]
    [InlineData("Mary-Anne Fernandes")]
    [InlineData("రమేష్")]            // Telugu: the rule is letters, not ASCII
    public void Real_names_are_accepted(string name) => Assert.Null(Validate(name, "a@b.co").Errors.GetValueOrDefault("name"));

    [Theory]
    [InlineData("", "Please enter your name.")]
    [InlineData("   ", "Please enter your name.")]
    [InlineData(null, "Please enter your name.")]
    [InlineData("R", "Name must be 2 to 100 characters.")]
    [InlineData("Ramesh99", "Use letters, spaces and . ' - only.")]
    [InlineData("<script>", "Use letters, spaces and . ' - only.")]
    [InlineData(" Leading", null)]   // trimmed first, so this one is fine
    public void Names_are_checked(string? name, string? expectedError)
    {
        var error = Validate(name, "a@b.co").Errors.GetValueOrDefault("name")?[0];
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void An_over_long_name_is_rejected_rather_than_cut()
    {
        var error = Validate(new string('a', ProfileValidator.MaxNameLength + 1), "a@b.co").Errors.GetValueOrDefault("name");
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("ramesh@example.com")]
    [InlineData("a.b+tag@sub.example.co.in")]
    public void Usable_addresses_are_accepted(string email) => Assert.Null(Validate("Ramesh", email).Errors.GetValueOrDefault("email"));

    [Theory]
    [InlineData("", "Please enter your email address.")]
    [InlineData(null, "Please enter your email address.")]
    [InlineData("nope", "That does not look like a valid email address.")]
    [InlineData("no@domain", "That does not look like a valid email address.")]
    [InlineData("@example.com", "That does not look like a valid email address.")]
    [InlineData("two spaces@example.com", "That does not look like a valid email address.")]
    public void Addresses_are_checked(string? email, string expectedError) =>
        Assert.Equal(expectedError, Validate("Ramesh", email).Errors.GetValueOrDefault("email")?[0]);

    [Fact]
    public void Every_bad_field_is_reported_at_once()
    {
        var result = Validate("", "nope");

        Assert.Null(result.Value);
        Assert.Equal(2, result.Errors.Count);
        Assert.True(result.Errors.ContainsKey("name"));
        Assert.True(result.Errors.ContainsKey("email"));
    }
}
