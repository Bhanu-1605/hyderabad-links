using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// Phase 0 baseline: proves the API starts, exposes health/meta, sets security headers,
// and does not grant CORS access to unlisted origins.
public class ApiBaselineTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_live_returns_ok()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Public_meta_returns_service_name()
    {
        var response = await _client.GetAsync("/api/v1/public/meta");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("BusinessDirectory.Api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_are_not_cacheable()
    {
        var response = await _client.GetAsync("/api/v1/public/meta");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task Unlisted_origin_receives_no_cors_permission()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/meta");
        request.Headers.Add("Origin", "https://evil.example");
        var response = await _client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
