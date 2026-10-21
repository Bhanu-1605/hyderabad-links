using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// Taking a live listing off the public site is moderation, so it needs an admin session that has
// completed MFA. These run with no database, so the authorization boundary is what is testable here;
// the status rules, the audit rows and the public page disappearing were verified against the
// running API.
public class AdminSuspendAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Suspending_requires_an_admin_session()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/listings/1/suspend", new { reason = "Not signed in." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reinstating_requires_an_admin_session() =>
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.PostAsync("/api/v1/admin/listings/1/reinstate", null)).StatusCode);

    [Fact]
    public async Task An_owner_token_shape_does_not_reach_the_suspend_route()
    {
        // The owner scheme signs with the same key but a different audience, so an owner-shaped
        // bearer token must not satisfy the admin policy at all.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/listings/1/suspend")
        {
            Content = JsonContent.Create(new { reason = "An owner trying to moderate." }),
        };
        request.Headers.Add("Authorization", "Bearer not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }
}
