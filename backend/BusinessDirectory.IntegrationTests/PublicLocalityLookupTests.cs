using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// Request validation for the two locality lookup endpoints behind the location autocomplete.
// Like the other integration tests here, these need no database: they prove the endpoints refuse
// bad input before it reaches SQL. What the queries actually return is checked against real rows
// by database/sqlserver/checks/locality_search_smoke.sql.
public class PublicLocalityLookupTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Search = "/api/v1/public/localities/search";
    private const string Nearest = "/api/v1/public/localities/nearest";

    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("")]            // no q at all
    [InlineData("?q=")]         // empty q
    [InlineData("?q=%20%20")]   // whitespace only
    [InlineData("?q=g")]        // a single letter would match most of the city
    public async Task A_lookup_without_enough_text_is_refused(string queryString) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(Search + queryString)).StatusCode);

    [Fact]
    public async Task A_refused_lookup_explains_itself_without_leaking_internals()
    {
        var response = await _client.GetAsync(Search + "?q=g");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("q must be at least", body);
        Assert.DoesNotContain("SqlClient", body);
        Assert.DoesNotContain("SELECT", body);
        Assert.DoesNotContain("ILIKE", body);
    }

    [Theory]
    [InlineData("")]                      // no coordinates
    [InlineData("?lat=17.4")]             // half a coordinate pair
    [InlineData("?lng=78.4")]
    [InlineData("?lat=123&lng=78.4")]     // latitude outside the world
    [InlineData("?lat=17.4&lng=-200")]    // longitude outside the world
    public async Task Nearest_refuses_anything_that_is_not_a_real_point(string queryString) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(Nearest + queryString)).StatusCode);

    [Fact]
    public async Task A_refused_point_explains_itself_without_leaking_internals()
    {
        var response = await _client.GetAsync(Nearest + "?lat=123&lng=78.4");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("lat must be between -90 and 90", body);
        Assert.DoesNotContain("SqlClient", body);
        Assert.DoesNotContain("ST_Distance", body);
    }
}
