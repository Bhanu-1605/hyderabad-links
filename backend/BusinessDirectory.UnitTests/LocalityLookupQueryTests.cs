using BusinessDirectory.Api.Listings;

namespace BusinessDirectory.UnitTests;

public class LocalitySearchQueryTests
{
    private static LocalitySearchQuery? Create(string? q = "gachi", string? city = null, int? limit = null) =>
        LocalitySearchQuery.TryCreate(q, city, limit, out _);

    private static string? ErrorFrom(string? q = null, int? limit = null)
    {
        LocalitySearchQuery.TryCreate(q, null, limit, out var error);
        return error;
    }

    [Fact]
    public void A_typed_fragment_is_accepted_with_the_default_limit()
    {
        var query = Create("gachi");

        Assert.NotNull(query);
        Assert.Equal("gachi", query.Text);
        Assert.Null(query.CitySlug);
        Assert.Equal(LocalitySearchQuery.DefaultLimit, query.Limit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("g")]
    [InlineData(" g ")] // one letter once trimmed
    public void Too_little_text_is_refused(string? q)
    {
        Assert.Null(Create(q));
        Assert.Equal($"q must be at least {LocalitySearchQuery.MinTextLength} characters.", ErrorFrom(q));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_from_the_text_and_the_city()
    {
        var query = Create("  kphb  ", city: "  hyderabad  ");

        Assert.NotNull(query);
        Assert.Equal("kphb", query.Text);
        Assert.Equal("hyderabad", query.CitySlug);
    }

    [Fact]
    public void A_blank_city_means_every_city()
    {
        Assert.Null(Create(city: "   ")!.CitySlug);
    }

    [Fact]
    public void Over_long_text_is_cut_rather_than_refused()
    {
        var query = Create(new string('a', LocalitySearchQuery.MaxTextLength + 50));

        Assert.NotNull(query);
        Assert.Equal(LocalitySearchQuery.MaxTextLength, query.Text.Length);
    }

    [Theory]
    [InlineData(null, LocalitySearchQuery.DefaultLimit)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5, 5)]
    [InlineData(500, LocalitySearchQuery.MaxLimit)]
    public void The_limit_is_clamped_rather_than_refused(int? limit, int expected)
    {
        Assert.Equal(expected, Create(limit: limit)!.Limit);
        Assert.Null(ErrorFrom(q: "gachi", limit: limit));
    }

    [Fact]
    public void Ordinary_text_becomes_a_prefix_and_a_contains_pattern()
    {
        var query = Create("gachi");

        Assert.NotNull(query);
        Assert.Equal("gachi%", query.PrefixPattern);
        Assert.Equal("%gachi%", query.ContainsPattern);
    }

    [Theory]
    // Without escaping, "%" would match every locality and "_" any single character.
    [InlineData("%", "\\%")]
    [InlineData("_", "\\_")]
    [InlineData("\\", "\\\\")]
    [InlineData("50% a_b", "50\\% a\\_b")]
    public void Like_wildcards_in_the_typed_text_are_escaped(string typed, string escaped)
    {
        // The minimum length rule needs two characters, so pad anything shorter.
        var text = typed.Length < LocalitySearchQuery.MinTextLength ? typed + "x" : typed;
        var expected = typed.Length < LocalitySearchQuery.MinTextLength ? escaped + "x" : escaped;

        var query = Create(text);

        Assert.NotNull(query);
        Assert.Equal(expected + "%", query.PrefixPattern);
        Assert.Equal("%" + expected + "%", query.ContainsPattern);
        // The trigram comparison uses the raw text, so it is not escaped.
        Assert.Equal(text, query.Text);
    }
}

public class NearestLocalityQueryTests
{
    private static string? ErrorFrom(double? lat, double? lng)
    {
        NearestLocalityQuery.TryCreate(lat, lng, out var error);
        return error;
    }

    [Fact]
    public void A_real_point_is_accepted()
    {
        var query = NearestLocalityQuery.TryCreate(17.4435, 78.3772, out var error);

        Assert.NotNull(query);
        Assert.Null(error);
        Assert.Equal(17.4435, query.Latitude);
        Assert.Equal(78.3772, query.Longitude);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(17.4, null)]
    [InlineData(null, 78.4)]
    public void Half_a_coordinate_pair_is_refused(double? lat, double? lng)
    {
        Assert.Null(NearestLocalityQuery.TryCreate(lat, lng, out _));
        Assert.Equal("lat and lng must be supplied together.", ErrorFrom(lat, lng));
    }

    [Theory]
    [InlineData(123, 78.4)]
    [InlineData(-91, 78.4)]
    [InlineData(17.4, 200)]
    [InlineData(17.4, -181)]
    public void A_point_outside_the_world_is_refused(double lat, double lng)
    {
        Assert.Null(NearestLocalityQuery.TryCreate(lat, lng, out _));
        Assert.Equal("lat must be between -90 and 90, and lng between -180 and 180.", ErrorFrom(lat, lng));
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    public void The_edges_of_the_world_are_allowed(double lat, double lng)
    {
        Assert.NotNull(NearestLocalityQuery.TryCreate(lat, lng, out _));
    }
}
