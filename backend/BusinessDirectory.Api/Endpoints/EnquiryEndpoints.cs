using System.Net;
using System.Security.Claims;
using BusinessDirectory.Api.Listings;
using Npgsql;
using NpgsqlTypes;

namespace BusinessDirectory.Api.Endpoints;

/// <summary>
/// Customer enquiries: one public endpoint to send one, and the owner's inbox.
///
/// The two halves live together because they are two views of one table, but they sit in different
/// route groups: sending is public and unauthenticated, reading is strictly the owner's.
/// </summary>
public static class EnquiryEndpoints
{
    public sealed record SendEnquiryRequest(string? Name, string? Phone, string? Email, string? Message);

    public sealed record SentDto(long Id);

    /// <param name="Phone">The sender's number. This is private customer data and never public.</param>
    public sealed record OwnerEnquiryDto(
        long Id,
        long ListingId,
        string ListingName,
        string Name,
        string Phone,
        string? Email,
        string Message,
        string Status,
        DateTime CreatedAt);

    public sealed record OwnerEnquiryPage(
        IReadOnlyList<OwnerEnquiryDto> Items,
        int Page,
        int PageSize,
        int Total,
        int TotalPages,
        int NewCount);

    public sealed record UpdateStatusRequest(string? Status);

    /// <summary>What an owner may set an enquiry to. 'new' is not here: it is the arrival state.</summary>
    private static readonly string[] SettableStatuses = ["read", "replied", "closed", "spam"];

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    /// <summary>
    /// Enquiries one sender may file in an hour, counted per IP. An enquiry costs the sender nothing
    /// and reaches a real person, so without a cap this is a spam channel.
    /// </summary>
    private const int MaxPerIpPerHour = 10;

    /// <summary>And per listing, so one business cannot be singled out for a flood.</summary>
    private const int MaxPerIpPerListingPerDay = 3;

    public static RouteGroupBuilder MapPublicEnquiryEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/listings/{citySlug}/{localitySlug}/{slug}/enquiries", Send);
        return group;
    }

    public static RouteGroupBuilder MapOwnerEnquiryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/enquiries", ListForOwner);
        group.MapPut("/enquiries/{id:long}/status", UpdateStatus);
        return group;
    }

    // ------------------------------------------------------------------ public: send one
    private static async Task<IResult> Send(
        string citySlug,
        string localitySlug,
        string slug,
        SendEnquiryRequest request,
        HttpContext http,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        var validation = EnquiryValidator.Validate(new EnquiryInput(request.Name, request.Phone, request.Email, request.Message));
        if (validation.Value is not { } enquiry)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Only a live listing can be contacted: a draft or rejected one is not public, so an enquiry
        // to it would be an enquiry to something the sender cannot have seen.
        long listingId;
        await using (var find = new NpgsqlCommand(
            @"SELECT li.id
              FROM listings li
              JOIN localities lo ON lo.id = li.locality_id
              JOIN cities ci ON ci.id = lo.city_id
              WHERE ci.slug = @city AND lo.slug = @locality AND li.slug = @slug AND li.status = 'active'", conn, tx))
        {
            find.Parameters.AddWithValue("city", citySlug);
            find.Parameters.AddWithValue("locality", localitySlug);
            find.Parameters.AddWithValue("slug", slug);
            if (await find.ExecuteScalarAsync(ct) is not long found)
            {
                return Results.NotFound();
            }
            listingId = found;
        }

        IPAddress? ip = http.Connection.RemoteIpAddress;
        if (ip is not null && await IsRateLimited(conn, tx, ip, listingId, ct))
        {
            return Results.Problem(
                "You have sent several enquiries already. Please try again later.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        long id;
        await using (var insert = new NpgsqlCommand(
            @"INSERT INTO enquiries (listing_id, name, phone_e164, email, message, ip)
              VALUES (@listing, @name, @phone, @email, @message, @ip)
              RETURNING id", conn, tx))
        {
            insert.Parameters.AddWithValue("listing", listingId);
            insert.Parameters.AddWithValue("name", enquiry.Name);
            insert.Parameters.AddWithValue("phone", enquiry.PhoneE164);
            insert.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = (object?)enquiry.Email ?? DBNull.Value });
            insert.Parameters.AddWithValue("message", enquiry.Message);
            insert.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = (object?)ip ?? DBNull.Value });
            id = (long)(await insert.ExecuteScalarAsync(ct))!;
        }

        await tx.CommitAsync(ct);

        // No notification is sent: there is no email or SMS provider configured yet, and claiming to
        // have told the owner would be worse than the owner finding it in their dashboard.
        return Results.Created($"/api/v1/public/enquiries/{id}", new SentDto(id));
    }

    private static async Task<bool> IsRateLimited(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        IPAddress ip,
        long listingId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT count(*) FILTER (WHERE created_at > now() - interval '1 hour'),
                     count(*) FILTER (WHERE created_at > now() - interval '1 day' AND listing_id = @listing)
              FROM enquiries
              WHERE ip = @ip", conn, tx);
        command.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = ip });
        command.Parameters.AddWithValue("listing", listingId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return reader.GetInt64(0) >= MaxPerIpPerHour || reader.GetInt64(1) >= MaxPerIpPerListingPerDay;
    }

    // ------------------------------------------------------------------ owner: read the inbox
    private static async Task<IResult> ListForOwner(
        string? status,
        int? page,
        int? pageSize,
        ClaimsPrincipal principal,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var wanted = status?.Trim().ToLowerInvariant();
        if (wanted is { Length: > 0 } && !SettableStatuses.Contains(wanted) && wanted != "new")
        {
            return Results.Problem("Unknown status filter.", statusCode: StatusCodes.Status400BadRequest);
        }

        var currentPage = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        // `owned` is the join that enforces ownership: every row returned belongs to a listing this
        // user is a member of, so no enquiry for anyone else's business can appear.
        await using var command = db.CreateCommand(
            @"WITH owned AS (
                  SELECT e.id, e.listing_id, li.name AS listing_name, e.name, e.phone_e164, e.email,
                         e.message, e.status, e.created_at
                  FROM enquiries e
                  JOIN listings li ON li.id = e.listing_id
                  JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = @user
              ),
              filtered AS (
                  SELECT * FROM owned WHERE @status::text IS NULL OR status = @status
              ),
              page AS (
                  SELECT * FROM filtered ORDER BY created_at DESC, id DESC LIMIT @size OFFSET @offset
              )
              SELECT (SELECT count(*) FROM filtered),
                     (SELECT count(*) FROM owned WHERE status = 'new'),
                     p.id, p.listing_id, p.listing_name, p.name, p.phone_e164, p.email,
                     p.message, p.status, p.created_at
              FROM (SELECT 1) AS always_one_row
              LEFT JOIN page p ON true
              ORDER BY p.created_at DESC, p.id DESC");
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text) { Value = (object?)wanted ?? DBNull.Value });
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("size", size);
        command.Parameters.AddWithValue("offset", (currentPage - 1) * size);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<OwnerEnquiryDto>();
        var total = 0;
        var newCount = 0;
        while (await reader.ReadAsync(ct))
        {
            total = (int)reader.GetInt64(0);
            newCount = (int)reader.GetInt64(1);
            if (reader.IsDBNull(2))
            {
                continue;   // the placeholder row: this page holds nothing
            }

            items.Add(new OwnerEnquiryDto(
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetDateTime(10)));
        }

        var totalPages = (int)Math.Ceiling(total / (double)size);
        return Results.Ok(new OwnerEnquiryPage(items, currentPage, size, total, totalPages, newCount));
    }

    // ------------------------------------------------------------------ owner: change its status
    private static async Task<IResult> UpdateStatus(
        long id,
        UpdateStatusRequest request,
        ClaimsPrincipal principal,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is null || !SettableStatuses.Contains(status))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"Status must be one of: {string.Join(", ", SettableStatuses)}."],
            });
        }

        // The ownership join is part of the UPDATE, so an id belonging to someone else matches no row
        // and answers 404 — the same answer as an id that does not exist, which tells a prober nothing.
        await using var command = db.CreateCommand(
            @"UPDATE enquiries e
              SET status = @status, updated_at = now()
              FROM listings li
              JOIN business_members bm ON bm.business_id = li.business_id
              WHERE e.id = @id AND li.id = e.listing_id AND bm.user_id = @user
              RETURNING e.id");
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);

        return await command.ExecuteScalarAsync(ct) is null ? Results.NotFound() : Results.NoContent();
    }
}
