using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// Request validation and access control only, so these never need a database connection.
public class OwnerAuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("+14155552671")]
    [InlineData("not a number")]
    public async Task Requesting_a_code_for_an_invalid_phone_is_rejected(string phone)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/owner/otp/request", new { phone });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requesting_a_code_without_a_body_is_rejected()
    {
        var response = await _client.PostAsync("/api/v1/auth/owner/otp/request", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("9876543210", "12345")]
    [InlineData("9876543210", "abcdef")]
    [InlineData("123", "123456")]
    public async Task Verifying_with_a_malformed_phone_or_code_is_rejected(string phone, string code)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/owner/otp/verify", new { phone, code });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refreshing_without_a_token_is_unauthorised()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/owner/refresh", new { refreshToken = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        var response = await _client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_rejects_a_forged_token()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIn0.invalidsignature");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
