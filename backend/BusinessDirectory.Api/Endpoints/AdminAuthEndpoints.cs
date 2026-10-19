using System.Net;
using System.Text.Json;
using BusinessDirectory.Api.Auth;
using Npgsql;
using NpgsqlTypes;

namespace BusinessDirectory.Api.Endpoints;

/// <summary>
/// Admin sign-in by email and password, under /api/v1/auth/admin. Raw parameterised SQL, no ORM.
/// Shaped like the owner's OTP sign-in (OwnerAuthEndpoints): a short-lived access token plus a rotating
/// refresh token whose hash is the only thing stored. Admin tokens carry the "admin" audience, so they are
/// refused on owner routes and owner tokens are refused here.
///
/// Admin accounts are invite-only: there is no sign-up endpoint. The first account is created by the
/// command-line bootstrap (see AdminBootstrap).
/// </summary>
public static class AdminAuthEndpoints
{
    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record RefreshRequest(string? RefreshToken);

    public sealed record SessionDto(string AccessToken, string RefreshToken, int ExpiresInSeconds);

    /// <summary>
    /// What a correct password alone earns: not a session, but a short-lived token for the second
    /// step. <paramref name="Stage"/> is "mfa" when the admin has an authenticator and "enroll" when
    /// they must set one up first, which is every admin's first sign-in.
    /// </summary>
    public sealed record MfaRequiredDto(string Stage, string MfaToken, int ExpiresInSeconds);

    /// <summary>
    /// One message for an unknown email, a wrong password and a locked or disabled account. It tells a
    /// locked-out admin enough to ask for help without telling an attacker which addresses exist.
    /// </summary>
    private const string SignInFailedMessage = "Email or password is incorrect, or the account is locked.";

    public static RouteGroupBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/admin");
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout);
        return group;
    }

    // ------------------------------------------------------------------ sign in
    private static async Task<IResult> Login(
        LoginRequest request,
        HttpContext http,
        NpgsqlDataSource db,
        AuthSettings settings,
        TokenService tokens,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrEmpty(request.Password) || request.Password.Length > 200)
        {
            AdminPasswords.VerifyAgainstDecoy(request.Password);
            return Results.Problem(SignInFailedMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        long adminId;
        string? storedHash;
        string status;
        int failedCount;
        bool locked;
        bool totpEnabled;
        await using (var find = new NpgsqlCommand(
            @"SELECT id, password_hash, status, failed_login_count, COALESCE(locked_until > now(), false),
                     totp_enabled_at IS NOT NULL
              FROM admin_users
              WHERE email = @email
              FOR UPDATE", conn, tx))
        {
            find.Parameters.AddWithValue("email", email);
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Unknown address: spend the same time as a real check, then answer the same way.
                AdminPasswords.VerifyAgainstDecoy(request.Password);
                await WriteAudit(conn, tx, actorAdminId: null, "admin.sign_in_failed", http, new { email, reason = "unknown_email" }, ct);
                await tx.CommitAsync(ct);
                return Results.Problem(SignInFailedMessage, statusCode: StatusCodes.Status401Unauthorized);
            }
            adminId = reader.GetInt64(0);
            storedHash = reader.IsDBNull(1) ? null : reader.GetString(1);
            status = reader.GetString(2);
            failedCount = reader.GetInt32(3);
            locked = reader.GetBoolean(4);
            totpEnabled = reader.GetBoolean(5);
        }

        // An invited account has no password yet, and a disabled one must not sign in. Both answer like a wrong password.
        if (locked || status != "active" || !AdminPasswords.Verify(request.Password, storedHash))
        {
            var reason = locked ? "locked" : status != "active" ? "not_active" : "wrong_password";
            if (!locked && status == "active")
            {
                // Count the failure and lock the account once the allowance is used up.
                await using var bump = new NpgsqlCommand(
                    @"UPDATE admin_users
                      SET failed_login_count = failed_login_count + 1,
                          locked_until = CASE WHEN failed_login_count + 1 >= @max
                                              THEN now() + make_interval(mins => @lockMinutes)
                                              ELSE locked_until END,
                          updated_at = now()
                      WHERE id = @id", conn, tx);
                bump.Parameters.AddWithValue("id", adminId);
                bump.Parameters.AddWithValue("max", settings.AdminMaxFailedLogins);
                // make_interval's `mins` parameter is an integer; passing a double makes Postgres fail to
                // resolve the function, which is why a wrong password used to answer 500.
                bump.Parameters.AddWithValue("lockMinutes", settings.AdminLockoutMinutes);
                await bump.ExecuteNonQueryAsync(ct);
            }

            await WriteAudit(conn, tx, adminId, "admin.sign_in_failed", http,
                new { reason, attempt = failedCount + 1 }, ct);
            await tx.CommitAsync(ct);
            return Results.Problem(SignInFailedMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        await using (var reset = new NpgsqlCommand(
            @"UPDATE admin_users
              SET failed_login_count = 0, locked_until = NULL, last_login_at = now(), updated_at = now()
              WHERE id = @id", conn, tx))
        {
            reset.Parameters.AddWithValue("id", adminId);
            await reset.ExecuteNonQueryAsync(ct);
        }

        // The password is right, and that is all. No session row and no admin token are created
        // here: MFA is mandatory, so the sign-in is only half done. What comes back reaches the
        // second step and nothing else, because it carries a different audience.
        var stage = totpEnabled ? "mfa" : "enroll";
        var (mfaToken, mfaExpiresIn) = totpEnabled
            ? tokens.CreateAdminMfaToken(adminId)
            : tokens.CreateAdminEnrollToken(adminId);

        await WriteAudit(conn, tx, adminId, "admin.password_verified", http, new { stage }, ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new MfaRequiredDto(stage, mfaToken, mfaExpiresIn));
    }

    // ------------------------------------------------------------------ rotate the refresh token
    private static async Task<IResult> Refresh(
        RefreshRequest request,
        HttpContext http,
        NpgsqlDataSource db,
        AuthSettings settings,
        TokenService tokens,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 200)
        {
            return Results.Unauthorized();
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        long sessionId;
        long adminId;
        Guid familyId;
        bool expired;
        bool alreadyUsed;
        DateTime? lastMfaAt;
        await using (var find = new NpgsqlCommand(
            @"SELECT id, admin_user_id, family_id, expires_at <= now(), (rotated_at IS NOT NULL OR revoked_at IS NOT NULL),
                     last_mfa_at
              FROM admin_sessions
              WHERE refresh_token_hash = @hash
              FOR UPDATE", conn, tx))
        {
            find.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = TokenService.HashRefreshToken(request.RefreshToken) });
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return Results.Unauthorized();
            }
            sessionId = reader.GetInt64(0);
            adminId = reader.GetInt64(1);
            familyId = reader.GetGuid(2);
            expired = reader.GetBoolean(3);
            alreadyUsed = reader.GetBoolean(4);
            lastMfaAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5);
        }

        // Every session is created by the MFA step, so last_mfa_at is always set. Checking it anyway
        // means a row that somehow appeared without MFA can never be refreshed into a live session.
        if (lastMfaAt is null)
        {
            return Results.Unauthorized();
        }

        if (alreadyUsed)
        {
            // A rotated or revoked token presented again means it was copied: end every session in the family.
            await using (var revoke = new NpgsqlCommand(
                "UPDATE admin_sessions SET revoked_at = now(), revoked_reason = 'reuse_detected' WHERE family_id = @family AND revoked_at IS NULL", conn, tx))
            {
                revoke.Parameters.AddWithValue("family", familyId);
                await revoke.ExecuteNonQueryAsync(ct);
            }
            await WriteAudit(conn, tx, adminId, "admin.session_reuse_detected", http, details: null, ct);
            await tx.CommitAsync(ct);
            return Results.Unauthorized();
        }

        if (expired)
        {
            return Results.Unauthorized();
        }

        await using (var status = new NpgsqlCommand("SELECT status FROM admin_users WHERE id = @id", conn, tx))
        {
            status.Parameters.AddWithValue("id", adminId);
            if ((string?)await status.ExecuteScalarAsync(ct) != "active")
            {
                return Results.Unauthorized();
            }
        }

        await using (var rotate = new NpgsqlCommand("UPDATE admin_sessions SET rotated_at = now() WHERE id = @id", conn, tx))
        {
            rotate.Parameters.AddWithValue("id", sessionId);
            await rotate.ExecuteNonQueryAsync(ct);
        }

        var newRefreshToken = TokenService.NewRefreshToken();
        await using (var insert = new NpgsqlCommand(
            @"INSERT INTO admin_sessions (admin_user_id, family_id, refresh_token_hash, expires_at, last_mfa_at, ip, user_agent)
              VALUES (@admin, @family, @hash, now() + make_interval(days => @days), @lastMfa, @ip, @ua)", conn, tx))
        {
            AddSessionParameters(insert, adminId, newRefreshToken, settings, http);
            insert.Parameters.AddWithValue("family", familyId);
            // Carried forward, not reset: refreshing is not a second authentication, so it must
            // neither re-stamp the MFA time nor quietly drop it.
            insert.Parameters.AddWithValue("lastMfa", lastMfaAt.Value);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        var (accessToken, expiresIn) = tokens.CreateAdminAccessToken(adminId);
        return Results.Ok(new SessionDto(accessToken, newRefreshToken, expiresIn));
    }

    // ------------------------------------------------------------------ sign out
    private static async Task<IResult> Logout(RefreshRequest request, NpgsqlDataSource db, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken) && request.RefreshToken.Length <= 200)
        {
            await using var command = db.CreateCommand(
                @"UPDATE admin_sessions SET revoked_at = now(), revoked_reason = 'logout'
                  WHERE revoked_at IS NULL
                    AND family_id = (SELECT family_id FROM admin_sessions WHERE refresh_token_hash = @hash)");
            command.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = TokenService.HashRefreshToken(request.RefreshToken) });
            await command.ExecuteNonQueryAsync(ct);
        }

        // Always 204, so the endpoint cannot be used to test whether a token exists.
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Stored addresses are lowercase (a CHECK constraint enforces it), so compare in lowercase.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 320)
        {
            return null;
        }

        var at = trimmed.IndexOf('@');
        if (at < 1 || at == trimmed.Length - 1 || trimmed.Contains(' ') || trimmed.IndexOf('@', at + 1) >= 0)
        {
            return null;
        }
        return trimmed;
    }

    /// <summary>Shared with <see cref="AdminMfaEndpoints"/>, which is where sessions are created now.</summary>
    internal static void AddSessionParameters(NpgsqlCommand command, long adminId, string refreshToken, AuthSettings settings, HttpContext http)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 300)
        {
            userAgent = userAgent[..300];
        }

        IPAddress? ip = http.Connection.RemoteIpAddress;
        command.Parameters.AddWithValue("admin", adminId);
        command.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = TokenService.HashRefreshToken(refreshToken) });
        command.Parameters.AddWithValue("days", settings.AdminRefreshTokenDays);
        command.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = (object?)ip ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ua", NpgsqlDbType.Text) { Value = string.IsNullOrEmpty(userAgent) ? DBNull.Value : userAgent });
    }

    /// <summary>
    /// Security events on the admin accounts themselves. A failed sign-in for an unknown address has no
    /// admin to attribute it to, so it is recorded as a system event. Passwords are never part of details.
    /// </summary>
    internal static async Task WriteAudit(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long? actorAdminId,
        string action,
        HttpContext http,
        object? details,
        CancellationToken ct)
    {
        IPAddress? ip = http.Connection.RemoteIpAddress;
        await using var command = new NpgsqlCommand(
            @"INSERT INTO audit_logs (actor_type, actor_id, action, entity_type, entity_id, details, ip)
              VALUES (@actorType, @actor, @action, 'admin_user', @entity, @details, @ip)", conn, tx);
        command.Parameters.AddWithValue("actorType", actorAdminId is null ? "system" : "admin");
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint) { Value = (object?)actorAdminId ?? DBNull.Value });
        command.Parameters.AddWithValue("action", action);
        command.Parameters.Add(new NpgsqlParameter("entity", NpgsqlDbType.Text)
        {
            Value = actorAdminId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (object)DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb)
        {
            Value = details is null ? DBNull.Value : JsonSerializer.Serialize(details),
        });
        command.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = (object?)ip ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
    }
}
