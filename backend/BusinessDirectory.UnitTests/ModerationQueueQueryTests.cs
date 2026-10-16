using BusinessDirectory.Api.Listings;

namespace BusinessDirectory.UnitTests;

public class ModerationQueueQueryTests
{
    private static ModerationQueueQuery? Create(string? status = null, int? page = null, int? pageSize = null) =>
        ModerationQueueQuery.TryCreate(status, page, pageSize, out _);

    [Fact]
    public void An_empty_request_shows_the_review_queue_oldest_first()
    {
        var query = Create();

        Assert.NotNull(query);
        Assert.Equal(ModerationQueueQuery.DefaultStatus, query.Status);
        Assert.Equal(1, query.Page);
        Assert.Equal(ModerationQueueQuery.DefaultPageSize, query.PageSize);
        Assert.Equal(0, query.Offset);
        Assert.True(query.OldestFirst);
    }

    [Theory]
    [InlineData("pending_review")]
    [InlineData("  ACTIVE  ")]
    [InlineData("Rejected")]
    [InlineData("draft")]
    [InlineData("suspended")]
    public void Every_allowed_status_is_accepted_whatever_the_case_and_spacing(string status)
    {
        var query = Create(status);

        Assert.NotNull(query);
        Assert.Contains(query.Status, ModerationQueueQuery.AllowedStatuses);
    }

    [Theory]
    [InlineData("archived")]            // deliberately out of scope for moderation
    [InlineData("nonsense")]
    [InlineData("pending_review'; DROP TABLE listings --")]
    public void An_unknown_status_is_refused_with_a_message(string status)
    {
        var query = ModerationQueueQuery.TryCreate(status, null, null, out var error);

        Assert.Null(query);
        Assert.NotNull(error);
        Assert.Contains("pending_review", error);
    }

    [Fact]
    public void Only_the_review_queue_is_ordered_oldest_first()
    {
        Assert.True(Create("pending_review")!.OldestFirst);
        Assert.False(Create("active")!.OldestFirst);
        Assert.False(Create("rejected")!.OldestFirst);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void Page_numbers_below_one_are_clamped_rather_than_refused(int? page, int expected)
    {
        Assert.Equal(expected, Create(page: page)!.Page);
    }

    [Theory]
    [InlineData(null, ModerationQueueQuery.DefaultPageSize)]
    [InlineData(0, ModerationQueueQuery.DefaultPageSize)]
    [InlineData(10, 10)]
    [InlineData(500, ModerationQueueQuery.MaxPageSize)]
    public void Page_size_is_clamped_so_nobody_can_ask_for_the_whole_table(int? pageSize, int expected)
    {
        Assert.Equal(expected, Create(pageSize: pageSize)!.PageSize);
    }

    [Fact]
    public void Offset_follows_the_page_and_page_size()
    {
        Assert.Equal(40, Create(page: 3, pageSize: 20)!.Offset);
        Assert.Equal(0, Create(page: 1, pageSize: 50)!.Offset);
    }
}

public class RejectionReasonTests
{
    [Fact]
    public void A_usable_reason_comes_back_trimmed()
    {
        var reason = RejectionReason.TryClean("  The phone number does not answer.  ", out var error);

        Assert.Equal("The phone number does not answer.", reason);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no")]
    [InlineData("too short")]   // 9 characters
    public void A_reason_that_tells_the_owner_nothing_is_refused(string? reason)
    {
        var cleaned = RejectionReason.TryClean(reason, out var error);

        Assert.Null(cleaned);
        Assert.NotNull(error);
    }

    [Fact]
    public void A_reason_longer_than_the_database_column_is_refused()
    {
        var cleaned = RejectionReason.TryClean(new string('x', RejectionReason.MaxLength + 1), out var error);

        Assert.Null(cleaned);
        Assert.Contains(RejectionReason.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture), error);
    }

    [Fact]
    public void A_reason_of_exactly_the_maximum_length_is_accepted()
    {
        Assert.NotNull(RejectionReason.TryClean(new string('x', RejectionReason.MaxLength), out _));
    }
}
