using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// The overview reports an owner's own figures, so it must refuse anonymous callers. Like the other
// integration tests here it needs no database; the counting itself, including that one owner's
// numbers never include another's listings, is covered by database/sqlserver/checks/owner_overview_smoke.sql.
public class OwnerOverviewAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task The_overview_requires_sign_in() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/me/overview")).StatusCode);

    [Fact]
    public async Task A_rejected_token_is_not_told_why()
    {
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-real-token");
        var response = await _client.GetAsync("/api/v1/me/overview");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("SqlClient", body);
        Assert.DoesNotContain("SELECT", body);
    }
}
