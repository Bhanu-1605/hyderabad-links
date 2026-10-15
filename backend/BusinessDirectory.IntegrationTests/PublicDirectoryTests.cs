using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// These tests exercise request validation only, so they never need a database connection.
public class PublicDirectoryTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Nearby_rejects_out_of_range_coordinates()
    {
        var response = await _client.GetAsync("/api/v1/public/listings/nearby?lat=123&lng=78.4");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nearby_requires_lat_and_lng()
    {
        var response = await _client.GetAsync("/api/v1/public/listings/nearby");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
