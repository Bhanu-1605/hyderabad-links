using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace BusinessDirectory.Api.Endpoints;

/// <summary>Endpoints for the signed-in business owner, under /api/v1/me. Every route requires an owner access token.</summary>
public static class MeEndpoints
{
    public sealed record MeDto(long Id, string Phone, string? Name);

    public static RouteGroupBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").RequireAuthorization("Owner");
        group.MapGet("", GetMe);
        group.MapOwnerListingEndpoints();
        return group;
    }

    /// <summary>Reads the owner's user id from the validated token.</summary>
    public static bool TryGetUserId(ClaimsPrincipal user, out long userId)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out userId);
    }

    private static async Task<IResult> GetMe(ClaimsPrincipal principal, NpgsqlDataSource db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var command = db.CreateCommand("SELECT phone_e164, name FROM users WHERE id = @id AND status = 'active'");
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new MeDto(userId, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
    }
}
