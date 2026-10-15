using System.Net;
using BusinessDirectory.Api.Auth;
using Npgsql;
using NpgsqlTypes;

namespace BusinessDirectory.Api.Endpoints;

/// <summary>
/// Business-owner login by phone OTP, under /api/v1/auth/owner. Raw parameterised SQL, no ORM.
/// Rate limits live in PostgreSQL (counted from otp_challenges), so they hold across several API instances.
/// The browser never calls these directly: the Next.js server calls them and keeps the tokens in httpOnly cookies.
/// </summary>
public static class OwnerAuthEndpoints
{
    public sealed record RequestOtpRequest(string? Phone);

    public sealed record VerifyOtpRequest(string? Phone, string? Code);

    public sealed record RefreshRequest(string? RefreshToken);

    public sealed record OtpRequestedDto(int ExpiresInSeconds, int ResendAfterSeconds);

    public sealed record SessionDto(string AccessToken, string RefreshToken, int ExpiresInSeconds);

    private const string InvalidCodeMessage = "The code is invalid or has expired.";

    public static RouteGroupBuilder MapOwnerAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/owner");
        group.MapPost("/otp/request", RequestOtp);
        group.MapPost("/otp/verify", VerifyOtp);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout);
        return group;
    }

    // ------------------------------------------------------------------ request a code
    private static async Task<IResult> RequestOtp(
        RequestOtpRequest request,
        HttpContext http,
        NpgsqlDataSource db,
        AuthSettings settings,
        ISmsSender sms,
        CancellationToken ct)
    {
        if (!PhoneNumber.TryNormalizeIndia(request.Phone, out var phone))
        {
            return Results.Problem("Enter a valid 10-digit Indian mobile number.", statusCode: StatusCodes.Status400BadRequest);
        }

        var ip = http.Connection.RemoteIpAddress;

        await using (var check = db.CreateCommand(
            @"SELECT
                (SELECT count(*) FROM otp_challenges WHERE phone_e164 = @phone AND created_at > now() - interval '1 hour'),
                COALESCE((SELECT max(created_at) FROM otp_challenges WHERE phone_e164 = @phone) > now() - make_interval(secs => @cooldown), false),
                (SELECT count(*) FROM otp_challenges WHERE requester_ip = @ip AND created_at > now() - interval '1 hour')"))
        {
            check.Parameters.AddWithValue("phone", phone);
            check.Parameters.AddWithValue("cooldown", (double)settings.ResendCooldownSeconds);
            check.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = (object?)ip ?? DBNull.Value });

            await using var reader = await check.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var perPhone = reader.GetInt64(0);
            var inCooldown = reader.GetBoolean(1);
            var perIp = reader.GetInt64(2);
            if (inCooldown || perPhone >= settings.MaxOtpPerPhonePerHour || perIp >= settings.MaxOtpPerIpPerHour)
            {
                http.Response.Headers.RetryAfter = settings.ResendCooldownSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Problem("Too many code requests. Please wait a little and try again.", statusCode: StatusCodes.Status429TooManyRequests);
            }
        }

        var code = OtpCodes.Generate();
        var hash = OtpCodes.Hash(settings.OtpKey, phone, code);

        await using (var insert = db.CreateCommand(
            @"INSERT INTO otp_challenges (phone_e164, code_hash, expires_at, requester_ip)
              VALUES (@phone, @hash, now() + make_interval(secs => @ttl), @ip)"))
        {
            insert.Parameters.AddWithValue("phone", phone);
            insert.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = hash });
            insert.Parameters.AddWithValue("ttl", (double)settings.OtpTtlSeconds);
            insert.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = (object?)ip ?? DBNull.Value });
            await insert.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await sms.SendOtpAsync(phone, code, ct);
        }
        catch (InvalidOperationException)
        {
            return Results.Problem("Login by phone is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Same response whether or not the number already has an account.
        return Results.Accepted(value: new OtpRequestedDto(settings.OtpTtlSeconds, settings.ResendCooldownSeconds));
    }

    // ------------------------------------------------------------------ verify the code
    private static async Task<IResult> VerifyOtp(
        VerifyOtpRequest request,
        HttpContext http,
        NpgsqlDataSource db,
        AuthSettings settings,
        TokenService tokens,
        CancellationToken ct)
    {
        if (!PhoneNumber.TryNormalizeIndia(request.Phone, out var phone) || !OtpCodes.IsWellFormed(request.Code))
        {
            return Results.Problem(InvalidCodeMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Lock the newest usable challenge for this phone so concurrent guesses are counted one at a time.
        long challengeId;
        byte[] storedHash;
        int attempts;
        int maxAttempts;
        await using (var find = new NpgsqlCommand(
            @"SELECT id, code_hash, attempts, max_attempts
              FROM otp_challenges
              WHERE phone_e164 = @phone AND consumed_at IS NULL AND expires_at > now()
              ORDER BY created_at DESC
              LIMIT 1
              FOR UPDATE", conn, tx))
        {
            find.Parameters.AddWithValue("phone", phone);
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return Results.Problem(InvalidCodeMessage, statusCode: StatusCodes.Status400BadRequest);
            }
            challengeId = reader.GetInt64(0);
            storedHash = reader.GetFieldValue<byte[]>(1);
            attempts = reader.GetInt16(2);
            maxAttempts = reader.GetInt16(3);
        }

        if (attempts >= maxAttempts)
        {
            return Results.Problem(InvalidCodeMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        // Count the attempt first, and commit it even when the guess is wrong.
        await using (var bump = new NpgsqlCommand("UPDATE otp_challenges SET attempts = attempts + 1 WHERE id = @id", conn, tx))
        {
            bump.Parameters.AddWithValue("id", challengeId);
            await bump.ExecuteNonQueryAsync(ct);
        }

        if (!OtpCodes.Verify(settings.OtpKey, phone, request.Code!, storedHash))
        {
            await tx.CommitAsync(ct);
            return Results.Problem(InvalidCodeMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        await using (var consume = new NpgsqlCommand("UPDATE otp_challenges SET consumed_at = now() WHERE id = @id AND consumed_at IS NULL", conn, tx))
        {
            consume.Parameters.AddWithValue("id", challengeId);
            if (await consume.ExecuteNonQueryAsync(ct) != 1)
            {
                return Results.Problem(InvalidCodeMessage, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        // First successful login creates the account.
        long userId;
        string userStatus;
        await using (var upsert = new NpgsqlCommand(
            @"INSERT INTO users (phone_e164, phone_verified_at) VALUES (@phone, now())
              ON CONFLICT (phone_e164) DO UPDATE
                SET phone_verified_at = COALESCE(users.phone_verified_at, now()), updated_at = now()
              RETURNING id, status", conn, tx))
        {
            upsert.Parameters.AddWithValue("phone", phone);
            await using var reader = await upsert.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            userId = reader.GetInt64(0);
            userStatus = reader.GetString(1);
        }

        if (userStatus != "active")
        {
            await tx.CommitAsync(ct);
            return Results.Problem("This account cannot sign in.", statusCode: StatusCodes.Status403Forbidden);
        }

        var refreshToken = TokenService.NewRefreshToken();
        await using (var session = new NpgsqlCommand(
            @"INSERT INTO user_sessions (user_id, refresh_token_hash, expires_at, ip, user_agent)
              VALUES (@user, @hash, now() + make_interval(days => @days), @ip, @ua)", conn, tx))
        {
            AddSessionParameters(session, userId, refreshToken, settings, http);
            await session.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        var (accessToken, expiresIn) = tokens.CreateAccessToken(userId);
        return Results.Ok(new SessionDto(accessToken, refreshToken, expiresIn));
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

        var presentedHash = TokenService.HashRefreshToken(request.RefreshToken);

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        long sessionId;
        long userId;
        Guid familyId;
        bool expired;
        bool alreadyUsed;
        await using (var find = new NpgsqlCommand(
            @"SELECT id, user_id, family_id, expires_at <= now(), (rotated_at IS NOT NULL OR revoked_at IS NOT NULL)
              FROM user_sessions
              WHERE refresh_token_hash = @hash
              FOR UPDATE", conn, tx))
        {
            find.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = presentedHash });
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return Results.Unauthorized();
            }
            sessionId = reader.GetInt64(0);
            userId = reader.GetInt64(1);
            familyId = reader.GetGuid(2);
            expired = reader.GetBoolean(3);
            alreadyUsed = reader.GetBoolean(4);
        }

        if (alreadyUsed)
        {
            // A token that was already rotated or revoked is being presented again: assume theft and end the whole family.
            await using var revoke = new NpgsqlCommand(
                "UPDATE user_sessions SET revoked_at = now(), revoked_reason = 'reuse_detected' WHERE family_id = @family AND revoked_at IS NULL", conn, tx);
            revoke.Parameters.AddWithValue("family", familyId);
            await revoke.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Unauthorized();
        }

        if (expired)
        {
            return Results.Unauthorized();
        }

        await using (var status = new NpgsqlCommand("SELECT status FROM users WHERE id = @id", conn, tx))
        {
            status.Parameters.AddWithValue("id", userId);
            if ((string?)await status.ExecuteScalarAsync(ct) != "active")
            {
                return Results.Unauthorized();
            }
        }

        await using (var rotate = new NpgsqlCommand("UPDATE user_sessions SET rotated_at = now() WHERE id = @id", conn, tx))
        {
            rotate.Parameters.AddWithValue("id", sessionId);
            await rotate.ExecuteNonQueryAsync(ct);
        }

        var newRefreshToken = TokenService.NewRefreshToken();
        await using (var insert = new NpgsqlCommand(
            @"INSERT INTO user_sessions (user_id, family_id, refresh_token_hash, expires_at, ip, user_agent)
              VALUES (@user, @family, @hash, now() + make_interval(days => @days), @ip, @ua)", conn, tx))
        {
            AddSessionParameters(insert, userId, newRefreshToken, settings, http);
            insert.Parameters.AddWithValue("family", familyId);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        var (accessToken, expiresIn) = tokens.CreateAccessToken(userId);
        return Results.Ok(new SessionDto(accessToken, newRefreshToken, expiresIn));
    }

    // ------------------------------------------------------------------ sign out
    private static async Task<IResult> Logout(RefreshRequest request, NpgsqlDataSource db, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken) && request.RefreshToken.Length <= 200)
        {
            await using var command = db.CreateCommand(
                @"UPDATE user_sessions SET revoked_at = now(), revoked_reason = 'logout'
                  WHERE revoked_at IS NULL
                    AND family_id = (SELECT family_id FROM user_sessions WHERE refresh_token_hash = @hash)");
            command.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = TokenService.HashRefreshToken(request.RefreshToken) });
            await command.ExecuteNonQueryAsync(ct);
        }

        // Always 204, so the endpoint cannot be used to test whether a token exists.
        return Results.NoContent();
    }

    private static void AddSessionParameters(NpgsqlCommand command, long userId, string refreshToken, AuthSettings settings, HttpContext http)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 300)
        {
            userAgent = userAgent[..300];
        }

        IPAddress? ip = http.Connection.RemoteIpAddress;
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = TokenService.HashRefreshToken(refreshToken) });
        command.Parameters.AddWithValue("days", settings.RefreshTokenDays);
        command.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = (object?)ip ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ua", NpgsqlDbType.Text) { Value = string.IsNullOrEmpty(userAgent) ? DBNull.Value : userAgent });
    }
}
