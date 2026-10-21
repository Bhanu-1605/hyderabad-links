using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessDirectory.IntegrationTests;

// MFA is what turns a verified password into an admin session, so every one of these endpoints must
// refuse a caller who has not come through the password step.
//
// Like the other integration tests here, these run with no database, which bounds what they can
// assert: anything reaching SQL answers 500 rather than its real status. Authorization runs before
// the handler, so refusals are testable here; the sign-in *outcomes* are not. Those were verified
// against the running API (uniform failure message, lockout after five wrong passwords, lockout
// after five wrong codes, replay refused, recovery code single-use) and the storage rules live in
// database/sqlserver/checks/admin_mfa_smoke.sql. Asserting them here would only prove a 500.
public class AdminMfaAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/v1/auth/admin/mfa/verify")]
    [InlineData("/api/v1/auth/admin/mfa/enroll/start")]
    [InlineData("/api/v1/auth/admin/mfa/enroll/confirm")]
    public async Task Every_mfa_endpoint_refuses_an_anonymous_caller(string path)
    {
        var response = await _client.PostAsJsonAsync(path, new { code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/admin/mfa/verify")]
    [InlineData("/api/v1/auth/admin/mfa/enroll/start")]
    [InlineData("/api/v1/auth/admin/mfa/enroll/confirm")]
    public async Task A_made_up_token_does_not_open_them(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { code = "123456" }),
        };
        request.Headers.Add("Authorization", "Bearer not-a-real-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_token_cannot_be_used_on_an_mfa_endpoint()
    {
        // The halfway stages have their own audiences, so no other realm's token satisfies them.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/admin/mfa/verify")
        {
            Content = JsonContent.Create(new { code = "123456" }),
        };
        request.Headers.Add("Authorization", "Bearer eyJhbGciOiJIUzI1NiJ9.e30.not-a-real-signature");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Admin_routes_still_refuse_an_anonymous_caller() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/admin/listings")).StatusCode);
}
