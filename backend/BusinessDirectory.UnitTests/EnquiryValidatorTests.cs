using BusinessDirectory.Api.Listings;

namespace BusinessDirectory.UnitTests;

public class EnquiryValidatorTests
{
    private const string GoodMessage = "Please call me about a leaking tap in my kitchen.";

    private static EnquiryValidationResult Validate(
        string? name = "Priya Sharma",
        string? phone = "9876543210",
        string? email = null,
        string? message = GoodMessage) =>
        EnquiryValidator.Validate(new EnquiryInput(name, phone, email, message));

    [Fact]
    public void A_complete_enquiry_is_accepted_and_normalised()
    {
        var result = Validate(name: "  Priya Sharma  ", phone: "98765 43210", email: "  Priya@Example.COM  ");

        Assert.Empty(result.Errors);
        Assert.Equal("Priya Sharma", result.Value!.Name);
        // E.164, matching the CHECK constraint in V0006.
        Assert.Equal("+919876543210", result.Value.PhoneE164);
        // Lowercased, because the column requires it.
        Assert.Equal("priya@example.com", result.Value.Email);
    }

    [Fact]
    public void Email_is_optional_because_a_phone_number_is_enough()
    {
        var result = Validate(email: "");

        Assert.Empty(result.Errors);
        Assert.Null(result.Value!.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string? name) =>
        Assert.Contains("name", Validate(name: name).Errors.Keys);

    [Fact]
    public void An_over_long_name_is_refused() =>
        Assert.Contains("name", Validate(name: new string('a', EnquiryValidator.MaxNameLength + 1)).Errors.Keys);

    [Theory]
    [InlineData("9876543210")]
    [InlineData("98765 43210")]
    [InlineData("+919876543210")]
    [InlineData("919876543210")]
    [InlineData("09876543210")]
    public void Indian_mobiles_are_accepted_however_they_are_typed(string phone) =>
        Assert.Equal("+919876543210", Validate(phone: phone).Value!.PhoneE164);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890")]      // does not start 6-9
    [InlineData("98765")]
    [InlineData("not a number")]
    public void Unusable_phone_numbers_are_refused(string? phone) =>
        Assert.Contains("phone", Validate(phone: phone).Errors.Keys);

    [Theory]
    [InlineData("nope")]
    [InlineData("no@domain")]
    [InlineData("@example.com")]
    [InlineData("two spaces@example.com")]
    public void Malformed_email_addresses_are_refused(string email) =>
        Assert.Contains("email", Validate(email: email).Errors.Keys);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hi")]
    [InlineData("too short")]
    public void A_message_must_say_something(string? message) =>
        Assert.Contains("message", Validate(message: message).Errors.Keys);

    [Fact]
    public void A_message_at_the_minimum_length_is_accepted() =>
        Assert.Empty(Validate(message: new string('a', EnquiryValidator.MinMessageLength)).Errors);

    [Fact]
    public void An_over_long_message_is_refused() =>
        Assert.Contains("message", Validate(message: new string('a', EnquiryValidator.MaxMessageLength + 1)).Errors.Keys);

    [Fact]
    public void Every_bad_field_is_reported_at_once()
    {
        var result = Validate(name: "", phone: "123", email: "nope", message: "hi");

        Assert.Null(result.Value);
        Assert.Equal(4, result.Errors.Count);
    }
}
