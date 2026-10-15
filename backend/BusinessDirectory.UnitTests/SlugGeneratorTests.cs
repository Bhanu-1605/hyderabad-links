using BusinessDirectory.Api.Listings;

namespace BusinessDirectory.UnitTests;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Sri Sai Plumbers", "sri-sai-plumbers")]
    [InlineData("  A & B   Repairs!! ", "a-b-repairs")]
    [InlineData("Ravi's 24/7 Taxi", "ravi-s-24-7-taxi")]
    [InlineData("CAFE   Nizam", "cafe-nizam")]
    public void Names_become_clean_slugs(string name, string expected) =>
        Assert.Equal(expected, SlugGenerator.FromName(name));

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("తెలుగు")]   // no ASCII letters at all
    public void Names_without_usable_characters_fall_back_to_business(string name) =>
        Assert.Equal("business", SlugGenerator.FromName(name));

    [Fact]
    public void Long_names_are_cut_without_a_trailing_hyphen()
    {
        var slug = SlugGenerator.FromName(string.Join(' ', Enumerable.Repeat("plumbers", 20)));
        Assert.True(slug.Length <= SlugGenerator.MaxLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void Generated_slugs_satisfy_the_database_format()
    {
        var pattern = new System.Text.RegularExpressions.Regex("^[a-z0-9]+(-[a-z0-9]+)*$");
        foreach (var name in new[] { "Sri Sai Plumbers", "A & B", "!!!", "24x7 Taxi", "--x--" })
        {
            Assert.Matches(pattern, SlugGenerator.FromName(name));
        }
    }

    [Fact]
    public void NextFree_returns_the_base_slug_when_free_and_numbers_otherwise()
    {
        Assert.Equal("sri-sai", SlugGenerator.NextFree("sri-sai", new HashSet<string>()));
        Assert.Equal("sri-sai-2", SlugGenerator.NextFree("sri-sai", new HashSet<string> { "sri-sai" }));
        Assert.Equal("sri-sai-3", SlugGenerator.NextFree("sri-sai", new HashSet<string> { "sri-sai", "sri-sai-2" }));
    }
}
