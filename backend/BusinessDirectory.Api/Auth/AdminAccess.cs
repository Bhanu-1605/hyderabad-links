using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace BusinessDirectory.Api.Auth;

/// <summary>The signed-in admin, as the database sees them right now.</summary>
public sealed record AdminIdentity(
    long Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// Turns a validated admin token into the permissions that admin holds *now*. Nothing about
/// permissions travels in the token, so disabling an account or removing a role takes effect on the
/// next request instead of when the token expires.
/// </summary>
public static class AdminAccess
{
    /// <summary>Second JWT scheme: same signing key, but it only accepts the admin audience.</summary>
    public const string Scheme = "AdminBearer";

    /// <summary>Authorization policy name. It pins the scheme, so an owner token cannot satisfy it.</summary>
    public const string Policy = "Admin";

    /// <summary>
    /// The halfway states of an admin sign-in. Each has its own scheme and policy so that a token
    /// issued after the password but before the authenticator code reaches only the endpoint it is
    /// meant for — and fails audience validation on every real admin route.
    /// </summary>
    public const string MfaScheme = "AdminMfaBearer";
    public const string MfaPolicy = "AdminMfa";
    public const string EnrollScheme = "AdminEnrollBearer";
    public const string EnrollPolicy = "AdminEnroll";

    private const string LoadSql =
        @"SELECT au.email,
                 au.display_name,
                 COALESCE(array_agg(DISTINCT r.slug) FILTER (WHERE r.slug IS NOT NULL), '{}'),
                 COALESCE(array_agg(DISTINCT p.code) FILTER (WHERE p.code IS NOT NULL), '{}')
          FROM admin_users au
          LEFT JOIN admin_user_roles aur ON aur.admin_user_id = au.id
          LEFT JOIN roles r ON r.id = aur.role_id
          LEFT JOIN role_permissions rp ON rp.role_id = r.id
          LEFT JOIN permissions p ON p.id = rp.permission_id
          WHERE au.id = @id AND au.status = 'active'
          GROUP BY au.id, au.email, au.display_name";

    public static bool TryGetAdminId(ClaimsPrincipal principal, out long adminUserId)
    {
        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out adminUserId);
    }

    /// <summary>Returns null when the token's subject is not an active admin any more.</summary>
    public static async Task<AdminIdentity?> Load(NpgsqlDataSource db, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!TryGetAdminId(principal, out var adminUserId))
        {
            return null;
        }

        await using var command = db.CreateCommand(LoadSql);
        command.Parameters.AddWithValue("id", adminUserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new AdminIdentity(
            adminUserId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetFieldValue<string[]>(2),
            reader.GetFieldValue<string[]>(3));
    }

    /// <summary>
    /// Returns null when the admin may go ahead, or the response to send back when they may not.
    /// A permission reserved for the super-admin surface is refused here even to someone who holds it:
    /// that capability must be reached with a super-admin token, not an admin one.
    /// </summary>
    public static IResult? Authorize(AdminIdentity admin, string permission)
    {
        if (!AdminPermissions.IsAdminSurface(permission) || !admin.Can(permission))
        {
            return Results.Problem("You do not have permission to do that.", statusCode: StatusCodes.Status403Forbidden);
        }
        return null;
    }
}
