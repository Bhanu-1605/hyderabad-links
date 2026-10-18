using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// The profile endpoint writes to the signed-in owner's own user row, so it must refuse anonymous
// callers before it validates anything. Like the other integration tests here it needs no database.
public class OwnerProfileAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Profile = "/api/v1/me/profile";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Updating_the_profile_requires_sign_in()
    {
        var response = await _client.PutAsJsonAsync(Profile, new { name = "Ramesh Kumar", email = "ramesh@example.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_learns_nothing_from_invalid_input()
    {
        // 401 rather than 400: an anonymous caller should not be able to probe the validation rules.
        var response = await _client.PutAsJsonAsync(Profile, new { name = "", email = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_phone_number_is_not_a_profile_field()
    {
        // Nothing here should accept a phone number: the verified number is the identity, and
        // changing it would mean re-verifying. A caller sending one must still be refused.
        var response = await _client.PutAsJsonAsync(Profile, new
        {
            name = "Ramesh Kumar",
            email = "ramesh@example.com",
            phone = "+919999000000",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
