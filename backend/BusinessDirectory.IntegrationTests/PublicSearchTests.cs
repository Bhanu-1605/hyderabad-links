using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// Request validation for /api/v1/public/listings/search. Like the other integration tests here,
// these need no database: they prove the endpoint refuses bad input before it reaches SQL.
// The result shapes themselves are checked against real data by database/checks/search_smoke.sql.
public class PublicSearchTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Search = "/api/v1/public/listings/search";

    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("?lat=123&lng=78.4")]            // latitude outside the world
    [InlineData("?lat=17.4&lng=-200")]           // longitude outside the world
    [InlineData("?lat=17.4")]                    // half a coordinate pair
    [InlineData("?lng=78.4")]
    [InlineData("?sort=price")]                  // not one of the three allowed orders
    [InlineData("?sort=distance")]               // distance ordering without a location
    [InlineData("?radiusKm=5")]                  // a radius without a location
    public async Task Unusable_parameters_are_refused(string queryString) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(Search + queryString)).StatusCode);

    [Fact]
    public async Task A_rejected_request_explains_itself_without_leaking_internals()
    {
        var response = await _client.GetAsync(Search + "?sort=price");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("sort must be one of", body);
        Assert.DoesNotContain("SqlClient", body);
        Assert.DoesNotContain("SELECT", body);
    }
}
