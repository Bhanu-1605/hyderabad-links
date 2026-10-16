using Npgsql;
using NpgsqlTypes;

namespace BusinessDirectory.Api.Listings;

/// <summary>
/// The SQL that creates and edits a listing, shared by the owner's screens and by an admin adding one
/// manually. Both paths must produce the same kind of row — same slug rules, same columns, same
/// category link — so the statements live here once instead of being written twice.
///
/// Every method takes an open connection and transaction: the caller decides what else belongs in the
/// same transaction (the audit row, for instance).
/// </summary>
public static class ListingWriter
{
    /// <summary>An area and its centre, used as the map point when the caller gives no coordinates.</summary>
    public sealed record LocalityPoint(int Id, double Latitude, double Longitude);

    public static async Task<LocalityPoint?> FindLocality(
        NpgsqlConnection conn, NpgsqlTransaction tx, string citySlug, string localitySlug, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT lo.id, ST_Y(lo.center::geometry), ST_X(lo.center::geometry)
              FROM localities lo JOIN cities ci ON ci.id = lo.city_id
              WHERE ci.slug = @city AND lo.slug = @locality AND ci.is_active AND lo.is_active", conn, tx);
        command.Parameters.AddWithValue("city", citySlug);
        command.Parameters.AddWithValue("locality", localitySlug);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new LocalityPoint(reader.GetInt32(0), reader.GetDouble(1), reader.GetDouble(2))
            : null;
    }

    /// <summary>Listings attach to sub-categories (level 2) only.</summary>
    public static async Task<int?> FindCategoryId(NpgsqlConnection conn, NpgsqlTransaction tx, string slug, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM categories WHERE slug = @slug AND level = 2 AND is_active", conn, tx);
        command.Parameters.AddWithValue("slug", slug);
        var result = await command.ExecuteScalarAsync(ct);
        return result is int id ? id : null;
    }

    /// <summary>Picks a free slug within the area: name, name-2, name-3 ... (slugs are unique per locality).</summary>
    public static async Task<string> ChooseSlug(NpgsqlConnection conn, NpgsqlTransaction tx, int localityId, string name, CancellationToken ct)
    {
        var baseSlug = SlugGenerator.FromName(name);
        var taken = new HashSet<string>();
        await using (var command = new NpgsqlCommand(
            "SELECT slug FROM listings WHERE locality_id = @locality AND (slug = @base OR slug LIKE @pattern)", conn, tx))
        {
            command.Parameters.AddWithValue("locality", localityId);
            command.Parameters.AddWithValue("base", baseSlug);
            command.Parameters.AddWithValue("pattern", baseSlug + "-%");
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                taken.Add(reader.GetString(0));
            }
        }
        return SlugGenerator.NextFree(baseSlug, taken);
    }

    public static async Task<long> InsertBusiness(NpgsqlConnection conn, NpgsqlTransaction tx, string name, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("INSERT INTO businesses (name) VALUES (@name) RETURNING id", conn, tx);
        command.Parameters.AddWithValue("name", name);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <param name="status">"draft" for an owner's new listing, "active" or "pending_review" for one an admin adds.</param>
    /// <param name="source">Where the row came from: "owner", "admin" or "import" (a CHECK constraint enforces it).</param>
    /// <param name="createdByUserId">The owner who created it, or null when an admin added it on the phone.</param>
    public static async Task<long> InsertListing(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long businessId,
        LocalityPoint locality,
        string slug,
        ValidListing value,
        string status,
        string source,
        long? createdByUserId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"INSERT INTO listings (business_id, locality_id, slug, name, description, address_line, pincode, location,
                                    phone_primary, phone_whatsapp, website_url, status, source, created_by_user_id, published_at)
              VALUES (@business, @locality, @slug, @name, @description, @address, @pincode,
                      ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
                      @phone, @whatsapp, @website, @status, @source, @user,
                      CASE WHEN @status = 'active' THEN now() ELSE NULL END)
              RETURNING id", conn, tx);
        command.Parameters.AddWithValue("business", businessId);
        command.Parameters.AddWithValue("locality", locality.Id);
        command.Parameters.AddWithValue("slug", slug);
        command.Parameters.AddWithValue("name", value.Name);
        AddText(command, "description", value.Description);
        AddText(command, "address", value.AddressLine);
        AddText(command, "pincode", value.Pincode);
        command.Parameters.AddWithValue("lng", value.Longitude ?? locality.Longitude);
        command.Parameters.AddWithValue("lat", value.Latitude ?? locality.Latitude);
        command.Parameters.AddWithValue("phone", value.PhonePrimary);
        AddText(command, "whatsapp", value.PhoneWhatsapp);
        AddText(command, "website", value.WebsiteUrl);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.Add(new NpgsqlParameter("user", NpgsqlDbType.Bigint) { Value = (object?)createdByUserId ?? DBNull.Value });
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Updates the editable fields. The area (and so the public URL) is never changed here, by either
    /// an owner or an admin: moving a listing to another area would break its links.
    /// </summary>
    public static async Task UpdateListing(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long listingId,
        ValidListing value,
        double currentLatitude,
        double currentLongitude,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"UPDATE listings SET
                  name = @name, description = @description, address_line = @address, pincode = @pincode,
                  phone_primary = @phone, phone_whatsapp = @whatsapp, website_url = @website,
                  location = ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
                  updated_at = now()
              WHERE id = @id", conn, tx);
        command.Parameters.AddWithValue("id", listingId);
        command.Parameters.AddWithValue("name", value.Name);
        AddText(command, "description", value.Description);
        AddText(command, "address", value.AddressLine);
        AddText(command, "pincode", value.Pincode);
        command.Parameters.AddWithValue("phone", value.PhonePrimary);
        AddText(command, "whatsapp", value.PhoneWhatsapp);
        AddText(command, "website", value.WebsiteUrl);
        // No new map point sent: keep the current one.
        command.Parameters.AddWithValue("lng", value.Longitude ?? currentLongitude);
        command.Parameters.AddWithValue("lat", value.Latitude ?? currentLatitude);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task AddPrimaryCategory(NpgsqlConnection conn, NpgsqlTransaction tx, long listingId, int categoryId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (@listing, @category, true)", conn, tx);
        command.Parameters.AddWithValue("listing", listingId);
        command.Parameters.AddWithValue("category", categoryId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Moves the listing to another primary category. Adds the link when the listing has none.</summary>
    public static async Task SetPrimaryCategory(NpgsqlConnection conn, NpgsqlTransaction tx, long listingId, int categoryId, CancellationToken ct)
    {
        await using (var update = new NpgsqlCommand(
            "UPDATE listing_categories SET category_id = @category WHERE listing_id = @listing AND is_primary", conn, tx))
        {
            update.Parameters.AddWithValue("listing", listingId);
            update.Parameters.AddWithValue("category", categoryId);
            if (await update.ExecuteNonQueryAsync(ct) > 0)
            {
                return;
            }
        }
        await AddPrimaryCategory(conn, tx, listingId, categoryId, ct);
    }

    public static void AddText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });
}
