using BusinessDirectory.Api.Listings;

namespace BusinessDirectory.UnitTests;

public class ListingValidatorTests
{
    private const string OwnerPhone = "+919876543210";

    private static ListingInput Valid(Func<ListingInput, ListingInput>? change = null)
    {
        var input = new ListingInput(
            Name: "  Sri Sai Plumbers ",
            Description: "Quick and reliable plumbing in Madhapur.",
            CitySlug: null,
            LocalitySlug: "madhapur",
            CategorySlug: "plumbers",
            AddressLine: "Plot 12, Madhapur",
            Pincode: "500081",
            PhonePrimary: null,
            PhoneWhatsapp: "98765 43211",
            WebsiteUrl: "example.com",
            Latitude: null,
            Longitude: null);
        return change is null ? input : change(input);
    }

    [Fact]
    public void A_good_listing_is_cleaned_and_normalised()
    {
        var result = ListingValidator.Validate(Valid(), requireLocality: true, OwnerPhone);

        Assert.NotNull(result.Value);
        Assert.Empty(result.Errors);
        Assert.Equal("Sri Sai Plumbers", result.Value!.Name);
        Assert.Equal("hyderabad", result.Value.CitySlug);
        Assert.Equal(OwnerPhone, result.Value.PhonePrimary);          // defaults to the login number
        Assert.Equal("+919876543211", result.Value.PhoneWhatsapp);
        Assert.Equal("https://example.com/", result.Value.WebsiteUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    public void Name_is_required(string? name)
    {
        var result = ListingValidator.Validate(Valid(i => i with { Name = name }), true, OwnerPhone);
        Assert.Null(result.Value);
        Assert.Contains("name", result.Errors.Keys);
    }

    [Fact]
    public void Name_over_120_characters_is_rejected()
    {
        var result = ListingValidator.Validate(Valid(i => i with { Name = new string('a', 121) }), true, OwnerPhone);
        Assert.Contains("name", result.Errors.Keys);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Shop > Rest")]
    public void Angle_brackets_are_rejected_in_text_fields(string text)
    {
        Assert.Contains("name", ListingValidator.Validate(Valid(i => i with { Name = text }), true, OwnerPhone).Errors.Keys);
        Assert.Contains("addressLine", ListingValidator.Validate(Valid(i => i with { AddressLine = text }), true, OwnerPhone).Errors.Keys);
        Assert.Contains("description", ListingValidator.Validate(Valid(i => i with { Description = text }), true, OwnerPhone).Errors.Keys);
    }

    [Fact]
    public void Description_may_contain_line_breaks_but_name_may_not()
    {
        Assert.NotNull(ListingValidator.Validate(Valid(i => i with { Description = "Line one.\nLine two." }), true, OwnerPhone).Value);
        Assert.Contains("name", ListingValidator.Validate(Valid(i => i with { Name = "Two\nLines" }), true, OwnerPhone).Errors.Keys);
    }

    [Fact]
    public void Locality_is_required_only_when_creating()
    {
        var create = ListingValidator.Validate(Valid(i => i with { LocalitySlug = null }), requireLocality: true, OwnerPhone);
        var edit = ListingValidator.Validate(Valid(i => i with { LocalitySlug = null }), requireLocality: false, OwnerPhone);

        Assert.Contains("localitySlug", create.Errors.Keys);
        Assert.NotNull(edit.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not A Slug")]
    public void Category_is_required_and_must_look_like_a_slug(string? category)
    {
        var result = ListingValidator.Validate(Valid(i => i with { CategorySlug = category }), true, OwnerPhone);
        Assert.Contains("categorySlug", result.Errors.Keys);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0500081")]
    [InlineData("abcdef")]
    public void Pincode_must_be_six_digits(string pincode)
    {
        var result = ListingValidator.Validate(Valid(i => i with { Pincode = pincode }), true, OwnerPhone);
        Assert.Contains("pincode", result.Errors.Keys);
    }

    [Fact]
    public void Primary_phone_is_normalised_when_given_and_rejected_when_bad()
    {
        var good = ListingValidator.Validate(Valid(i => i with { PhonePrimary = "+91 90000 11111" }), true, OwnerPhone);
        var bad = ListingValidator.Validate(Valid(i => i with { PhonePrimary = "12345" }), true, OwnerPhone);

        Assert.Equal("+919000011111", good.Value!.PhonePrimary);
        Assert.Contains("phonePrimary", bad.Errors.Keys);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com")]
    [InlineData("localhost")]
    [InlineData("not a url")]
    public void Website_must_be_a_http_or_https_address(string url)
    {
        var result = ListingValidator.Validate(Valid(i => i with { WebsiteUrl = url }), true, OwnerPhone);
        Assert.Contains("websiteUrl", result.Errors.Keys);
    }

    [Fact]
    public void Map_point_must_be_inside_hyderabad_and_complete()
    {
        var inside = ListingValidator.Validate(Valid(i => i with { Latitude = 17.45, Longitude = 78.39 }), true, OwnerPhone);
        var outside = ListingValidator.Validate(Valid(i => i with { Latitude = 28.6, Longitude = 77.2 }), true, OwnerPhone);
        var half = ListingValidator.Validate(Valid(i => i with { Latitude = 17.45 }), true, OwnerPhone);

        Assert.NotNull(inside.Value);
        Assert.Contains("latitude", outside.Errors.Keys);
        Assert.Contains("latitude", half.Errors.Keys);
    }

    // An admin adding a listing on someone's behalf has no owner number to fall back on.
    [Fact]
    public void Without_a_fallback_number_the_phone_must_be_given()
    {
        var missing = ListingValidator.Validate(Valid(), requireLocality: true, fallbackPhone: null);

        Assert.Null(missing.Value);
        Assert.Contains("phonePrimary", missing.Errors.Keys);
    }

    [Fact]
    public void Without_a_fallback_number_a_typed_phone_is_enough()
    {
        var typed = ListingValidator.Validate(Valid(i => i with { PhonePrimary = "98765 43212" }), requireLocality: true, fallbackPhone: null);

        Assert.NotNull(typed.Value);
        Assert.Equal("+919876543212", typed.Value.PhonePrimary);
    }

    [Fact]
    public void A_typed_phone_still_wins_over_the_owners_number()
    {
        var typed = ListingValidator.Validate(Valid(i => i with { PhonePrimary = "98765 43212" }), true, OwnerPhone);

        Assert.NotNull(typed.Value);
        Assert.Equal("+919876543212", typed.Value.PhonePrimary);
    }
}
