using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// An owner's enquiries are private customer data — names, numbers and messages — so the inbox must
// refuse anonymous callers. Sending one is deliberately public and needs no account.
//
// These run with no database, so only the authorization boundary is testable here. That one owner
// never sees another's enquiries was verified against the running API with two real owners, and the
// storage rules are in database/checks/enquiry_smoke.sql.
public class EnquiryAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Reading_the_inbox_requires_sign_in() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/me/enquiries")).StatusCode);

    [Fact]
    public async Task Changing_an_enquiry_status_requires_sign_in()
    {
        var response = await _client.PutAsJsonAsync("/api/v1/me/enquiries/1/status", new { status = "read" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_made_up_token_does_not_open_the_inbox()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/enquiries");
        request.Headers.Add("Authorization", "Bearer not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Sending_an_enquiry_is_public_and_validates_before_touching_the_database()
    {
        // No database here, so a valid enquiry could not be stored. Invalid input is refused by the
        // validator first, which proves the endpoint is reachable without a token and that bad input
        // never reaches SQL.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/public/listings/hyderabad/madhapur/some-business/enquiries",
            new { name = "", phone = "123", email = "nope", message = "hi" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("name", body);
        Assert.Contains("phone", body);
        Assert.Contains("message", body);
        // Nothing internal leaks in the problem response.
        Assert.DoesNotContain("SqlClient", body);
        Assert.DoesNotContain("INSERT", body);
    }
}
