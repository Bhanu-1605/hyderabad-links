using System.Net;
using System.Security.Claims;
using BusinessDirectory.Api.Listings;
using Npgsql;
using NpgsqlTypes;

namespace BusinessDirectory.Api.Endpoints;

/// <summary>
/// A signed-in owner's own listings, under /api/v1/me/listings. Raw parameterised SQL, no ORM.
/// Every query is scoped to the caller through business_members, so another owner's listing looks like "not found".
/// New listings start as "draft"; the owner submits them for review. Approval (draft -> active) is an admin action (Phase 5).
/// </summary>
public static class OwnerListingEndpoints
{
    public const int MaxListingsPerOwner = 5;

    public sealed record OwnerListingDto(
        long Id,
        string Slug,
        string Name,
        string? Description,
        string? AddressLine,
        string? Pincode,
        string PhonePrimary,
        string? PhoneWhatsapp,
        string? WebsiteUrl,
        double Latitude,
        double Longitude,
        string CitySlug,
        string LocalitySlug,
        string LocalityName,
        string CategorySlug,
        string CategoryName,
        string Status,
        string Url);

    private const string OwnedListingSelect =
        @"SELECT li.id, li.slug, li.name, li.description, li.address_line, li.pincode, li.phone_primary, li.phone_whatsapp,
                 li.website_url, ST_Y(li.location::geometry), ST_X(li.location::geometry),
                 ci.slug, lo.slug, lo.name, pc.slug, pc.name, li.status
          FROM listings li
          JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = @user
          JOIN localities lo ON lo.id = li.locality_id
          JOIN cities ci ON ci.id = lo.city_id
          JOIN listing_categories lc ON lc.listing_id = li.id AND lc.is_primary
          JOIN categories pc ON pc.id = lc.category_id
          WHERE li.status <> 'archived'";

    public static RouteGroupBuilder MapOwnerListingEndpoints(this RouteGroupBuilder me)
    {
        var group = me.MapGroup("/listings");
        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapGet("/{id:long}", Get);
        group.MapPut("/{id:long}", Update);
        group.MapPost("/{id:long}/submit", Submit);
        return group;
    }

    // ------------------------------------------------------------------ list / get
    private static async Task<IResult> List(ClaimsPrincipal principal, NpgsqlDataSource db, CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var command = db.CreateCommand(OwnedListingSelect + " ORDER BY li.created_at DESC");
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var listings = new List<OwnerListingDto>();
        while (await reader.ReadAsync(ct))
        {
            listings.Add(Read(reader));
        }
        return Results.Ok(listings);
    }

    private static async Task<IResult> Get(long id, ClaimsPrincipal principal, NpgsqlDataSource db, CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        var listing = await LoadOwned(conn, null, userId, id, ct);
        return listing is null ? Results.NotFound() : Results.Ok(listing);
    }

    // ------------------------------------------------------------------ create
    private static async Task<IResult> Create(
        ListingInput input,
        ClaimsPrincipal principal,
        HttpContext http,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var ownerPhone = await GetOwnerPhone(conn, tx, userId, ct);
        if (ownerPhone is null)
        {
            return Results.Unauthorized();
        }

        var validation = ListingValidator.Validate(input, requireLocality: true, fallbackPhone: ownerPhone);
        if (validation.Value is not { } value)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        long existing;
        await using (var count = new NpgsqlCommand(
            @"SELECT count(*) FROM listings li
              JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = @user
              WHERE li.status <> 'archived'", conn, tx))
        {
            count.Parameters.AddWithValue("user", userId);
            existing = (long)(await count.ExecuteScalarAsync(ct))!;
        }
        if (existing >= MaxListingsPerOwner)
        {
            return Results.Problem($"You can have at most {MaxListingsPerOwner} listings.", statusCode: StatusCodes.Status409Conflict);
        }

        int localityId;
        double centerLat;
        double centerLng;
        await using (var locality = new NpgsqlCommand(
            @"SELECT lo.id, ST_Y(lo.center::geometry), ST_X(lo.center::geometry)
              FROM localities lo JOIN cities ci ON ci.id = lo.city_id
              WHERE ci.slug = @city AND lo.slug = @locality AND ci.is_active AND lo.is_active", conn, tx))
        {
            locality.Parameters.AddWithValue("city", value.CitySlug);
            locality.Parameters.AddWithValue("locality", value.LocalitySlug!);
            await using var reader = await locality.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["localitySlug"] = ["Unknown area."] });
            }
            localityId = reader.GetInt32(0);
            centerLat = reader.GetDouble(1);
            centerLng = reader.GetDouble(2);
        }

        var categoryId = await FindCategoryId(conn, tx, value.CategorySlug, ct);
        if (categoryId is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["categorySlug"] = ["Unknown category."] });
        }

        // Choose a free slug within the area: name, name-2, name-3 ...
        var baseSlug = SlugGenerator.FromName(value.Name);
        var taken = new HashSet<string>();
        await using (var slugs = new NpgsqlCommand(
            "SELECT slug FROM listings WHERE locality_id = @locality AND (slug = @base OR slug LIKE @pattern)", conn, tx))
        {
            slugs.Parameters.AddWithValue("locality", localityId);
            slugs.Parameters.AddWithValue("base", baseSlug);
            slugs.Parameters.AddWithValue("pattern", baseSlug + "-%");
            await using var reader = await slugs.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                taken.Add(reader.GetString(0));
            }
        }
        var slug = SlugGenerator.NextFree(baseSlug, taken);

        long listingId;
        try
        {
            long businessId;
            await using (var business = new NpgsqlCommand("INSERT INTO businesses (name) VALUES (@name) RETURNING id", conn, tx))
            {
                business.Parameters.AddWithValue("name", value.Name);
                businessId = (long)(await business.ExecuteScalarAsync(ct))!;
            }

            await using (var member = new NpgsqlCommand(
                "INSERT INTO business_members (business_id, user_id, role) VALUES (@business, @user, 'owner')", conn, tx))
            {
                member.Parameters.AddWithValue("business", businessId);
                member.Parameters.AddWithValue("user", userId);
                await member.ExecuteNonQueryAsync(ct);
            }

            await using (var listing = new NpgsqlCommand(
                @"INSERT INTO listings (business_id, locality_id, slug, name, description, address_line, pincode, location,
                                        phone_primary, phone_whatsapp, website_url, status, source, created_by_user_id)
                  VALUES (@business, @locality, @slug, @name, @description, @address, @pincode,
                          ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
                          @phone, @whatsapp, @website, 'draft', 'owner', @user)
                  RETURNING id", conn, tx))
            {
                listing.Parameters.AddWithValue("business", businessId);
                listing.Parameters.AddWithValue("locality", localityId);
                listing.Parameters.AddWithValue("slug", slug);
                listing.Parameters.AddWithValue("name", value.Name);
                AddText(listing, "description", value.Description);
                AddText(listing, "address", value.AddressLine);
                AddText(listing, "pincode", value.Pincode);
                listing.Parameters.AddWithValue("lng", value.Longitude ?? centerLng);
                listing.Parameters.AddWithValue("lat", value.Latitude ?? centerLat);
                listing.Parameters.AddWithValue("phone", value.PhonePrimary);
                AddText(listing, "whatsapp", value.PhoneWhatsapp);
                AddText(listing, "website", value.WebsiteUrl);
                listing.Parameters.AddWithValue("user", userId);
                listingId = (long)(await listing.ExecuteScalarAsync(ct))!;
            }

            await using (var link = new NpgsqlCommand(
                "INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (@listing, @category, true)", conn, tx))
            {
                link.Parameters.AddWithValue("listing", listingId);
                link.Parameters.AddWithValue("category", categoryId.Value);
                await link.ExecuteNonQueryAsync(ct);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Two owners picked the same name in the same area at the same moment. Ask the caller to retry.
            return Results.Problem("That name was just taken in this area. Please try again.", statusCode: StatusCodes.Status409Conflict);
        }

        await WriteAudit(conn, tx, userId, "listing.created", listingId, http, ct);
        var dto = await LoadOwned(conn, tx, userId, listingId, ct);
        await tx.CommitAsync(ct);
        return Results.Created($"/api/v1/me/listings/{listingId}", dto);
    }

    // ------------------------------------------------------------------ update
    private static async Task<IResult> Update(
        long id,
        ListingInput input,
        ClaimsPrincipal principal,
        HttpContext http,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var ownerPhone = await GetOwnerPhone(conn, tx, userId, ct);
        if (ownerPhone is null)
        {
            return Results.Unauthorized();
        }

        var lockResult = await LockOwned(conn, tx, userId, id, ct);
        if (lockResult is null)
        {
            return Results.NotFound();
        }
        if (lockResult.Value.Status == "suspended")
        {
            return Results.Problem("This listing is suspended. Please contact support.", statusCode: StatusCodes.Status403Forbidden);
        }

        // The area (and so the URL) cannot be changed here; it is ignored on edit.
        var validation = ListingValidator.Validate(input, requireLocality: false, fallbackPhone: ownerPhone);
        if (validation.Value is not { } value)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        var categoryId = await FindCategoryId(conn, tx, value.CategorySlug, ct);
        if (categoryId is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["categorySlug"] = ["Unknown category."] });
        }

        await using (var update = new NpgsqlCommand(
            @"UPDATE listings SET
                  name = @name, description = @description, address_line = @address, pincode = @pincode,
                  phone_primary = @phone, phone_whatsapp = @whatsapp, website_url = @website,
                  location = ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
                  updated_at = now()
              WHERE id = @id", conn, tx))
        {
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("name", value.Name);
            AddText(update, "description", value.Description);
            AddText(update, "address", value.AddressLine);
            AddText(update, "pincode", value.Pincode);
            update.Parameters.AddWithValue("phone", value.PhonePrimary);
            AddText(update, "whatsapp", value.PhoneWhatsapp);
            AddText(update, "website", value.WebsiteUrl);
            // No new map point sent: keep the current one.
            update.Parameters.AddWithValue("lng", value.Longitude ?? lockResult.Value.Longitude);
            update.Parameters.AddWithValue("lat", value.Latitude ?? lockResult.Value.Latitude);
            await update.ExecuteNonQueryAsync(ct);
        }

        await using (var category = new NpgsqlCommand(
            "UPDATE listing_categories SET category_id = @category WHERE listing_id = @id AND is_primary", conn, tx))
        {
            category.Parameters.AddWithValue("id", id);
            category.Parameters.AddWithValue("category", categoryId.Value);
            await category.ExecuteNonQueryAsync(ct);
        }

        await WriteAudit(conn, tx, userId, "listing.updated", id, http, ct);
        var dto = await LoadOwned(conn, tx, userId, id, ct);
        await tx.CommitAsync(ct);
        return Results.Ok(dto);
    }

    // ------------------------------------------------------------------ submit for review
    private static async Task<IResult> Submit(
        long id,
        ClaimsPrincipal principal,
        HttpContext http,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        if (!MeEndpoints.TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var lockResult = await LockOwned(conn, tx, userId, id, ct);
        if (lockResult is null)
        {
            return Results.NotFound();
        }
        if (lockResult.Value.Status is not ("draft" or "rejected"))
        {
            return Results.Problem("Only draft or rejected listings can be submitted.", statusCode: StatusCodes.Status409Conflict);
        }

        var listing = await LoadOwned(conn, tx, userId, id, ct);
        if (listing is null)
        {
            return Results.NotFound();
        }

        // What a listing needs before a reviewer sees it.
        var missing = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(listing.AddressLine))
        {
            missing["addressLine"] = ["Add the business address before submitting."];
        }
        if (listing.Description is null || listing.Description.Length < 20)
        {
            missing["description"] = ["Add a short description (at least 20 characters) before submitting."];
        }
        if (missing.Count > 0)
        {
            return Results.ValidationProblem(missing);
        }

        await using (var submit = new NpgsqlCommand("UPDATE listings SET status = 'pending_review', updated_at = now() WHERE id = @id", conn, tx))
        {
            submit.Parameters.AddWithValue("id", id);
            await submit.ExecuteNonQueryAsync(ct);
        }

        await WriteAudit(conn, tx, userId, "listing.submitted", id, http, ct);
        var dto = await LoadOwned(conn, tx, userId, id, ct);
        await tx.CommitAsync(ct);
        return Results.Ok(dto);
    }

    // ------------------------------------------------------------------ helpers
    private static OwnerListingDto Read(NpgsqlDataReader r)
    {
        var city = r.GetString(11);
        var locality = r.GetString(12);
        var category = r.GetString(14);
        var slug = r.GetString(1);
        return new OwnerListingDto(
            r.GetInt64(0),
            slug,
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.GetDouble(9),
            r.GetDouble(10),
            city,
            locality,
            r.GetString(13),
            category,
            r.GetString(15),
            r.GetString(16),
            $"/{city}/{locality}/{category}/{slug}");
    }

    private static async Task<OwnerListingDto?> LoadOwned(NpgsqlConnection conn, NpgsqlTransaction? tx, long userId, long listingId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(OwnedListingSelect + " AND li.id = @id", conn, tx);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("id", listingId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>Locks the row so two edits cannot interleave. Returns null if the listing is not the caller's.</summary>
    private static async Task<(string Status, double Latitude, double Longitude)?> LockOwned(
        NpgsqlConnection conn, NpgsqlTransaction tx, long userId, long listingId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT li.status, ST_Y(li.location::geometry), ST_X(li.location::geometry)
              FROM listings li
              JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = @user
              WHERE li.id = @id AND li.status <> 'archived'
              FOR UPDATE OF li", conn, tx);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("id", listingId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2)) : null;
    }

    private static async Task<string?> GetOwnerPhone(NpgsqlConnection conn, NpgsqlTransaction tx, long userId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT phone_e164 FROM users WHERE id = @id AND status = 'active'", conn, tx);
        command.Parameters.AddWithValue("id", userId);
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    /// <summary>Listings attach to sub-categories (level 2) only.</summary>
    private static async Task<int?> FindCategoryId(NpgsqlConnection conn, NpgsqlTransaction tx, string slug, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM categories WHERE slug = @slug AND level = 2 AND is_active", conn, tx);
        command.Parameters.AddWithValue("slug", slug);
        var result = await command.ExecuteScalarAsync(ct);
        return result is int id ? id : null;
    }

    private static async Task WriteAudit(NpgsqlConnection conn, NpgsqlTransaction tx, long userId, string action, long listingId, HttpContext http, CancellationToken ct)
    {
        IPAddress? ip = http.Connection.RemoteIpAddress;
        await using var command = new NpgsqlCommand(
            @"INSERT INTO audit_logs (actor_type, actor_id, action, entity_type, entity_id, ip)
              VALUES ('owner', @user, @action, 'listing', @entity, @ip)", conn, tx);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity", listingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.Add(new NpgsqlParameter("ip", NpgsqlDbType.Inet) { Value = (object?)ip ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });
}
