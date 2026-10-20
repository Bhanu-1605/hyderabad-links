using System.Security.Claims;
using BusinessDirectory.Api.Media;
using Npgsql;

namespace BusinessDirectory.Api.Endpoints;

/// <summary>
/// A listing's photos, logo and video, under /api/v1/me/listings/{id}/media.
///
/// Three things are enforced here and nowhere else:
///   * ownership — every statement joins business_members, so another owner's listing is "not found";
///   * the plan's allowance — counted inside the same transaction that inserts the row, so two
///     uploads racing cannot both slip past the last free slot;
///   * what the file actually is — decided from its first bytes, never from the name or the
///     Content-Type the caller sent.
/// </summary>
public static class OwnerMediaEndpoints
{
    public sealed record MediaItemDto(
        long Id,
        string Kind,
        string Url,
        string ContentType,
        long ByteSize,
        int? Width,
        int? Height,
        int SortOrder,
        /// <summary>D10: over-allowance media after a downgrade. Kept, not deleted, and not shown publicly.</summary>
        bool IsHidden,
        DateTimeOffset CreatedAt);

    /// <param name="LimitsConfirmed">
    /// False while the per-tier allowances are still provisional (see V0008). The owner screens say so
    /// rather than presenting an unconfirmed number as a promise.
    /// </param>
    /// <param name="UploadsAvailable">
    /// False when no storage is configured; the UI then explains it instead of offering a button that cannot work.
    /// </param>
    public sealed record MediaAllowanceDto(
        string PlanSlug,
        string PlanName,
        int PhotoLimit,
        int VideoLimit,
        int LogoLimit,
        int PhotosUsed,
        int VideosUsed,
        int LogosUsed,
        bool LimitsConfirmed,
        bool UploadsAvailable);

    public sealed record MediaDto(MediaAllowanceDto Allowance, List<MediaItemDto> Items);

    public static RouteGroupBuilder MapOwnerMediaEndpoints(this RouteGroupBuilder listings)
    {
        var group = listings.MapGroup("/{id:long}/media");
        group.MapGet("", List);
        // The form is read from HttpRequest by hand, which also keeps the endpoint clear of minimal-API
        // antiforgery metadata: this API is authenticated by a bearer token, not by a cookie, so a
        // cross-site form post cannot carry the caller's credentials in the first place.
        group.MapPost("", Upload);
        group.MapDelete("/{mediaId:long}", Delete);
        return group;
    }

    // ------------------------------------------------------------------ list
    private static async Task<IResult> List(
        long id, ClaimsPrincipal principal, NpgsqlDataSource db, IMediaStorage storage, CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        var allowance = await ReadAllowance(conn, null, userId, id, storage.IsConfigured, ct);
        if (allowance is null)
        {
            return Results.NotFound();
        }

        var items = await ReadItems(conn, id, storage, ct);
        return Results.Ok(new MediaDto(allowance, items));
    }

    // ------------------------------------------------------------------ upload
    private static async Task<IResult> Upload(
        long id,
        HttpRequest request,
        ClaimsPrincipal principal,
        NpgsqlDataSource db,
        IMediaStorage storage,
        ILogger<OwnerMediaUpload> logger,
        CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        if (!storage.IsConfigured)
        {
            // Nothing is written to the database either: a row pointing at bytes that were never
            // stored would show as a permanently broken image.
            return Results.Problem("Uploads are not available right now.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!request.HasFormContentType)
        {
            return Results.Problem("Send the file as multipart/form-data.", statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var form = await request.ReadFormAsync(ct);
        if (!Enum.TryParse<MediaKind>(form["kind"], ignoreCase: true, out var kind))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Choose logo, photo or video."] });
        }

        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose a file."] });
        }

        var maxBytes = MediaFile.MaxBytesFor(kind);
        if (file.Length > maxBytes)
        {
            return Results.Problem(
                $"That file is {file.Length / (1024 * 1024)} MB. The limit is {maxBytes / (1024 * 1024)} MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        // Enough of the start of the file to identify it and, for an image, read its dimensions.
        var head = new byte[64 * 1024];
        int headLength;
        await using (var peek = file.OpenReadStream())
        {
            headLength = await peek.ReadAtLeastAsync(
                head, Math.Min(head.Length, (int)file.Length), throwOnEndOfStream: false, ct);
        }

        var contentType = MediaFile.SniffContentType(head.AsSpan(0, headLength));
        var matchesKind = kind == MediaKind.Video
            ? contentType == MediaFile.Mp4
            : MediaFile.ImageTypes.Contains(contentType);
        if (contentType is null || !matchesKind)
        {
            return Results.Problem(
                kind == MediaKind.Video ? "Videos must be MP4." : "Photos must be JPEG, PNG or WebP.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var size = kind == MediaKind.Video ? null : MediaFile.ReadImageSize(head.AsSpan(0, headLength), contentType);
        var storageKey = MediaFile.NewStorageKey(id, kind, contentType);

        await using var conn = await db.OpenConnectionAsync(ct);
        // The whole check-and-insert runs in one transaction, and the listing row is locked first, so
        // the count a concurrent upload sees already includes this one.
        await using var tx = await conn.BeginTransactionAsync(ct);

        var allowance = await ReadAllowance(conn, tx, userId, id, uploadsAvailable: true, ct);
        if (allowance is null)
        {
            return Results.NotFound();
        }

        var (limit, used) = kind switch
        {
            MediaKind.Logo => (allowance.LogoLimit, allowance.LogosUsed),
            MediaKind.Photo => (allowance.PhotoLimit, allowance.PhotosUsed),
            _ => (allowance.VideoLimit, allowance.VideosUsed),
        };
        if (used >= limit)
        {
            var what = kind.ToString().ToLowerInvariant();
            return Results.Problem(
                limit == 0
                    ? $"The {allowance.PlanName} plan does not include {what}s."
                    : $"The {allowance.PlanName} plan allows {limit} {what}{(limit == 1 ? "" : "s")}. Remove one first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        long mediaId;
        await using (var insert = new NpgsqlCommand(
            @"INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, width, height, sort_order, created_by_user_id)
              VALUES (@listing, @kind, @key, @type, @size, @width, @height,
                      COALESCE((SELECT max(sort_order) + 1 FROM listing_media WHERE listing_id = @listing AND kind = @kind), 0),
                      @user)
              RETURNING id", conn, tx))
        {
            insert.Parameters.AddWithValue("listing", id);
            insert.Parameters.AddWithValue("kind", kind.ToString().ToLowerInvariant());
            insert.Parameters.AddWithValue("key", storageKey);
            insert.Parameters.AddWithValue("type", contentType);
            insert.Parameters.AddWithValue("size", file.Length);
            insert.Parameters.AddWithValue("width", (object?)size?.Width ?? DBNull.Value);
            insert.Parameters.AddWithValue("height", (object?)size?.Height ?? DBNull.Value);
            insert.Parameters.AddWithValue("user", userId);
            mediaId = (long)(await insert.ExecuteScalarAsync(ct))!;
        }

        // The bytes are stored only after the row is reserved, and the row is committed only once the
        // bytes are safely there. A failure in between rolls the row back, so the gallery never lists
        // a file that is not in storage.
        try
        {
            await using var content = file.OpenReadStream();
            await storage.SaveAsync(storageKey, contentType, content, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload to media storage failed for listing {ListingId}.", id);
            await tx.RollbackAsync(CancellationToken.None);
            return Results.Problem("The file could not be stored. Please try again.", statusCode: StatusCodes.Status502BadGateway);
        }

        try
        {
            await tx.CommitAsync(ct);
        }
        catch (Exception)
        {
            // The bytes are stored but the row is gone, so nothing would ever reference them again.
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            throw;
        }

        return Results.Created($"/api/v1/me/listings/{id}/media/{mediaId}", new MediaItemDto(
            mediaId, kind.ToString().ToLowerInvariant(), storage.PublicUrl(storageKey), contentType,
            file.Length, size?.Width, size?.Height, 0, false, DateTimeOffset.UtcNow));
    }

    // ------------------------------------------------------------------ delete
    private static async Task<IResult> Delete(
        long id, long mediaId, ClaimsPrincipal principal, NpgsqlDataSource db, IMediaStorage storage, CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        // The join is the authorisation: a media id belonging to someone else's listing deletes nothing.
        await using var command = db.CreateCommand(
            @"DELETE FROM listing_media m
              USING listings li
              JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = @user
              WHERE m.id = @media AND m.listing_id = @listing AND li.id = m.listing_id
              RETURNING m.storage_key");
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("media", mediaId);
        command.Parameters.AddWithValue("listing", id);

        var storageKey = (string?)await command.ExecuteScalarAsync(ct);
        if (storageKey is null)
        {
            return Results.NotFound();
        }

        // The row is already gone. If removing the bytes fails, the only consequence is a blob nobody
        // references, which is better than a listing still showing a photo the owner deleted.
        if (storage.IsConfigured)
        {
            await storage.DeleteAsync(storageKey, ct);
        }
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ shared reads
    /// <summary>
    /// The listing's plan and what it has used so far, or null when this user does not own the listing.
    /// Passed a transaction, it also locks the listing row, which is what makes the quota check reliable.
    /// FOR NO KEY UPDATE rather than FOR UPDATE: it blocks a second uploader without blocking the
    /// foreign keys that point at the listing (its media, enquiries and reviews).
    /// </summary>
    private static async Task<MediaAllowanceDto?> ReadAllowance(
        NpgsqlConnection conn, NpgsqlTransaction? tx, long userId, long listingId, bool uploadsAvailable, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT p.slug, p.name, p.photo_limit, p.video_limit, p.logo_limit, p.media_limits_confirmed,
                     (SELECT count(*) FROM listing_media m WHERE m.listing_id = li.id AND m.kind = 'photo' AND NOT m.is_hidden),
                     (SELECT count(*) FROM listing_media m WHERE m.listing_id = li.id AND m.kind = 'video' AND NOT m.is_hidden),
                     (SELECT count(*) FROM listing_media m WHERE m.listing_id = li.id AND m.kind = 'logo' AND NOT m.is_hidden)
              FROM listings li
              JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = @user
              JOIN plans p ON p.slug = li.plan_slug
              WHERE li.id = @listing AND li.status <> 'archived'" + (tx is null ? "" : " FOR NO KEY UPDATE OF li"),
            conn, tx);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("listing", listingId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new MediaAllowanceDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            PhotosUsed: (int)reader.GetInt64(6),
            VideosUsed: (int)reader.GetInt64(7),
            LogosUsed: (int)reader.GetInt64(8),
            LimitsConfirmed: reader.GetBoolean(5),
            UploadsAvailable: uploadsAvailable);
    }

    private static async Task<List<MediaItemDto>> ReadItems(
        NpgsqlConnection conn, long listingId, IMediaStorage storage, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT id, kind, storage_key, content_type, byte_size, width, height, sort_order, is_hidden, created_at
              FROM listing_media
              WHERE listing_id = @listing
              ORDER BY kind, sort_order, id", conn);
        command.Parameters.AddWithValue("listing", listingId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<MediaItemDto>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new MediaItemDto(
                reader.GetInt64(0),
                reader.GetString(1),
                storage.PublicUrl(reader.GetString(2)),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetBoolean(8),
                reader.GetFieldValue<DateTimeOffset>(9)));
        }
        return items;
    }
}

/// <summary>Only a name for the upload logger, so the log category reads sensibly.</summary>
public sealed class OwnerMediaUpload;
