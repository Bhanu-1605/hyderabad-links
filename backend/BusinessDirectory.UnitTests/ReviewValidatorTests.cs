using BusinessDirectory.Api.Listings;

namespace BusinessDirectory.UnitTests;

public class ReviewValidatorTests
{
    private const string GoodBody = "Good filter coffee and the staff were quick and polite.";

    private static ReviewValidationResult Validate(int? rating = 4, string? body = GoodBody) =>
        ReviewValidator.Validate(new ReviewInput(rating, body));

    [Fact]
    public void A_complete_review_is_accepted_and_trimmed()
    {
        var result = Validate(body: $"  {GoodBody}  ");

        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Value!.Rating);
        Assert.Equal(GoodBody, result.Value.Body);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_rating_in_range_is_accepted(int rating) =>
        Assert.Equal((short)rating, Validate(rating: rating).Value!.Rating);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Ratings_outside_one_to_five_are_refused(int? rating) =>
        Assert.Contains("rating", Validate(rating: rating).Errors.Keys);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Great!")]
    [InlineData("Nineteen characters")]   // one short of the minimum
    public void A_review_too_short_to_help_anyone_is_refused(string? body) =>
        Assert.Contains("body", Validate(body: body).Errors.Keys);

    [Fact]
    public void A_review_at_exactly_the_minimum_length_is_accepted() =>
        Assert.Empty(Validate(body: new string('a', ReviewValidator.MinBodyLength)).Errors);

    [Fact]
    public void An_over_long_review_is_refused() =>
        Assert.Contains("body", Validate(body: new string('a', ReviewValidator.MaxBodyLength + 1)).Errors.Keys);

    [Fact]
    public void Whitespace_does_not_count_towards_the_minimum() =>
        Assert.Contains("body", Validate(body: "short" + new string(' ', 50)).Errors.Keys);

    [Fact]
    public void Both_bad_fields_are_reported_at_once()
    {
        var result = Validate(rating: 9, body: "hi");

        Assert.Null(result.Value);
        Assert.Equal(2, result.Errors.Count);
    }
}
