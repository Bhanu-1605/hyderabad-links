using BusinessDirectory.Api.Notifications;

namespace BusinessDirectory.UnitTests;

// What the emails actually say. The two rules worth testing are that both bodies carry the real
// content (nothing invented), and that no link appears when there is no site address to build it
// from - a mail client cannot follow "/account/enquiries".
public class EmailTemplatesTests
{
    private const string SiteUrl = "https://hyderabadlinks.com";

    private static readonly DateTimeOffset Received = new(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    private static EmailTemplates.NewEnquiry AnEnquiry(string? customerEmail = null, string message = "My kitchen tap is leaking badly.") =>
        new("Ravi Plumbing", "Priya Sharma", "+919876543210", customerEmail, message, Received,
            ListingPath: "/hyderabad/madhapur/plumbers/ravi-plumbing");

    // ------------------------------------------------------------------ a new enquiry
    [Fact]
    public void The_enquiry_email_carries_the_business_and_the_customer_in_both_bodies()
    {
        var email = EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry(), SiteUrl);

        Assert.Equal("owner@example.com", email.To);
        Assert.Equal("New enquiry for Ravi Plumbing", email.Subject);

        foreach (var body in new[] { email.TextBody, email.HtmlBody })
        {
            Assert.Contains("Ravi Plumbing", body);
            Assert.Contains("Priya Sharma", body);
            Assert.Contains("+919876543210", body);
            Assert.Contains("My kitchen tap is leaking badly.", body);
        }
    }

    [Fact]
    public void The_enquiry_email_shows_when_it_arrived_in_the_reader_s_own_time()
    {
        var email = EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry(), SiteUrl);

        // 09:30 UTC is 15:00 in Hyderabad, where everyone reading this is.
        Assert.Contains("20 Oct 2026, 3:00 PM IST", email.TextBody);
        Assert.Contains("20 Oct 2026, 3:00 PM IST", email.HtmlBody);
    }

    [Fact]
    public void The_customer_s_email_is_shown_only_when_they_gave_one()
    {
        var withEmail = EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry("priya@example.com"), SiteUrl);
        Assert.Contains("priya@example.com", withEmail.TextBody);
        Assert.Contains("mailto:priya@example.com", withEmail.HtmlBody);

        var without = EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry(), SiteUrl);
        Assert.DoesNotContain("Email:", without.TextBody);
        Assert.DoesNotContain("mailto:", without.HtmlBody);
    }

    [Fact]
    public void The_enquiry_email_links_to_the_inbox_that_exists()
    {
        var email = EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry(), SiteUrl);

        Assert.Contains("https://hyderabadlinks.com/account/enquiries", email.TextBody);
        Assert.Contains("https://hyderabadlinks.com/account/enquiries", email.HtmlBody);
    }

    [Fact]
    public void Without_a_site_address_the_enquiry_email_carries_no_link_at_all()
    {
        var email = EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry(), siteBaseUrl: null);

        // A relative path would be dead text in a mail client, so the link is left out instead.
        Assert.DoesNotContain("/account/enquiries", email.TextBody);
        Assert.DoesNotContain("/account/enquiries", email.HtmlBody);
        // The content the owner needs is still all there.
        Assert.Contains("Priya Sharma", email.TextBody);
    }

    [Fact]
    public void A_trailing_slash_on_the_site_address_does_not_double_up()
    {
        var email = EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry(), "https://hyderabadlinks.com/");

        Assert.Contains("https://hyderabadlinks.com/account/enquiries", email.TextBody);
        Assert.DoesNotContain("com//account", email.TextBody);
    }

    [Fact]
    public void A_customer_s_message_cannot_inject_markup_into_the_html_body()
    {
        var email = EmailTemplates.NewEnquiryEmail(
            "owner@example.com",
            AnEnquiry(message: "<script>alert('x')</script> & a quote \" here"),
            SiteUrl);

        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
        // The plain-text part is not markup, so it keeps the message exactly as sent.
        Assert.Contains("<script>alert('x')</script>", email.TextBody);
    }

    // ------------------------------------------------------------------ approved
    [Fact]
    public void The_approval_email_says_it_is_live_and_links_to_the_page()
    {
        var email = EmailTemplates.ListingApprovedEmail(
            "owner@example.com",
            new EmailTemplates.ListingDecision("Ravi Plumbing", "/hyderabad/madhapur/plumbers/ravi-plumbing", Reason: null),
            SiteUrl);

        Assert.Equal("Ravi Plumbing is now live on HYDERABAD LINKS", email.Subject);
        foreach (var body in new[] { email.TextBody, email.HtmlBody })
        {
            Assert.Contains("Ravi Plumbing", body);
            Assert.Contains("https://hyderabadlinks.com/hyderabad/madhapur/plumbers/ravi-plumbing", body);
        }
    }

    [Fact]
    public void An_approved_listing_with_no_public_url_yet_gets_no_link()
    {
        // A listing without a primary category has no public address, so there is nothing to link to.
        var email = EmailTemplates.ListingApprovedEmail(
            "owner@example.com",
            new EmailTemplates.ListingDecision("Ravi Plumbing", ListingPath: null, Reason: null),
            SiteUrl);

        Assert.DoesNotContain("hyderabadlinks.com", email.TextBody);
        Assert.DoesNotContain("href", email.HtmlBody);
        Assert.Contains("Ravi Plumbing", email.TextBody);
    }

    [Fact]
    public void The_approval_email_invents_no_numbers()
    {
        var email = EmailTemplates.ListingApprovedEmail(
            "owner@example.com",
            new EmailTemplates.ListingDecision("Ravi Plumbing", "/hyderabad/madhapur/plumbers/ravi-plumbing", Reason: null),
            SiteUrl);

        // Nothing counts views or searches yet, so the email must not imply that it does.
        Assert.DoesNotContain("views", email.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dashboard", email.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ rejected
    [Fact]
    public void The_rejection_email_quotes_the_moderator_s_reason_as_written()
    {
        const string reason = "The phone number does not answer and the address is outside Hyderabad.";
        var email = EmailTemplates.ListingRejectedEmail(
            "owner@example.com",
            new EmailTemplates.ListingDecision("Ravi Plumbing", "/hyderabad/madhapur/plumbers/ravi-plumbing", reason),
            SiteUrl);

        Assert.Equal("Ravi Plumbing has not been approved", email.Subject);
        foreach (var body in new[] { email.TextBody, email.HtmlBody })
        {
            Assert.Contains(reason, body);
            Assert.Contains("submit it again", body);
        }
        Assert.Contains("https://hyderabadlinks.com/account/listings", email.TextBody);
    }

    [Fact]
    public void A_rejection_with_no_reason_still_says_what_happened()
    {
        var email = EmailTemplates.ListingRejectedEmail(
            "owner@example.com",
            new EmailTemplates.ListingDecision("Ravi Plumbing", null, Reason: "   "),
            SiteUrl);

        Assert.Contains("has not been approved", email.TextBody);
        // No empty "the moderator gave this reason:" with nothing after it.
        Assert.DoesNotContain("gave this reason", email.TextBody);
        Assert.DoesNotContain("gave this reason", email.HtmlBody);
    }

    [Fact]
    public void A_moderator_s_reason_cannot_inject_markup_either()
    {
        var email = EmailTemplates.ListingRejectedEmail(
            "owner@example.com",
            new EmailTemplates.ListingDecision("Ravi & Sons <Plumbing>", null, "Duplicate of <b>another</b> listing."),
            SiteUrl);

        Assert.DoesNotContain("<b>another</b>", email.HtmlBody);
        Assert.Contains("Ravi &amp; Sons &lt;Plumbing&gt;", email.HtmlBody);
        Assert.Contains("Ravi & Sons <Plumbing>", email.TextBody);
    }

    // ------------------------------------------------------------------ shape
    [Fact]
    public void Every_template_produces_both_bodies_and_a_subject()
    {
        var decision = new EmailTemplates.ListingDecision("Ravi Plumbing", "/a/b/c/d", "A reason.");
        EmailMessage[] all =
        [
            EmailTemplates.NewEnquiryEmail("owner@example.com", AnEnquiry(), SiteUrl),
            EmailTemplates.ListingApprovedEmail("owner@example.com", decision, SiteUrl),
            EmailTemplates.ListingRejectedEmail("owner@example.com", decision, SiteUrl),
        ];

        foreach (var email in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(email.Subject));
            Assert.False(string.IsNullOrWhiteSpace(email.TextBody));
            Assert.StartsWith("<!doctype html>", email.HtmlBody);
            Assert.EndsWith("</html>", email.HtmlBody);
            // The plain-text part is text, not stripped HTML.
            Assert.DoesNotContain("<p>", email.TextBody);
        }
    }
}
