using BusinessDirectory.Api.Endpoints;

namespace BusinessDirectory.UnitTests;

public class OwnerOverviewTests
{
    private static OwnerOverviewEndpoints.ProfileCompletionDto Build(
        bool hasName = true,
        bool hasEmail = true,
        int totalListings = 1,
        int withDescription = 1,
        int withAddress = 1,
        int activeListings = 1) =>
        OwnerOverviewEndpoints.BuildCompletion(hasName, hasEmail, totalListings, withDescription, withAddress, activeListings);

    [Fact]
    public void A_brand_new_owner_has_nothing_done()
    {
        var completion = Build(hasName: false, hasEmail: false, totalListings: 0, withDescription: 0, withAddress: 0, activeListings: 0);

        Assert.Equal(0, completion.Completed);
        Assert.Equal(6, completion.Total);
        Assert.All(completion.Steps, step => Assert.False(step.Done));
    }

    [Fact]
    public void An_owner_who_has_done_everything_is_complete()
    {
        var completion = Build();

        Assert.Equal(completion.Total, completion.Completed);
        Assert.All(completion.Steps, step => Assert.True(step.Done));
    }

    [Fact]
    public void A_freshly_registered_owner_has_the_first_three_steps_done()
    {
        // Registration sets the name and email and creates one draft listing, but that listing has
        // no address or description yet and is not live.
        var completion = Build(totalListings: 1, withDescription: 0, withAddress: 0, activeListings: 0);

        Assert.Equal(3, completion.Completed);
        Assert.Equal(["name", "email", "listing"], completion.Steps.Where(s => s.Done).Select(s => s.Key));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("listing")]
    [InlineData("address")]
    [InlineData("description")]
    [InlineData("published")]
    public void Every_step_is_reported_with_a_label(string key)
    {
        var step = Build().Steps.Single(s => s.Key == key);
        Assert.False(string.IsNullOrWhiteSpace(step.Label));
    }

    [Fact]
    public void Counting_never_exceeds_the_number_of_steps()
    {
        // Several listings can satisfy the same step; it still counts once.
        var completion = Build(totalListings: 9, withDescription: 9, withAddress: 9, activeListings: 9);

        Assert.Equal(completion.Total, completion.Completed);
        Assert.Equal(6, completion.Total);
    }
}
