using Npgsql;
using NpgsqlTypes;

namespace BusinessDirectory.Api.Endpoints;

/// <summary>Read-only public directory endpoints under /api/v1/public. Raw parameterised SQL, no ORM.</summary>
public static class PublicDirectoryEndpoints
{
    public sealed record CityDto(string Slug, string Name, string State);

    public sealed record LocalityDto(string Slug, string Name, double Latitude, double Longitude);

    public sealed record SubCategoryDto(string Slug, string Name, string[] SearchTerms);

    public sealed record CategoryDto(string Slug, string Name, List<SubCategoryDto> Children);

    public sealed record NearbyListingDto(
        string Slug,
        string Name,
        string CitySlug,
        string LocalitySlug,
        string LocalityName,
        string CategorySlug,
        string CategoryName,
        int DistanceMetres,
        string? AddressLine,
        string PhonePrimary,
        string? PhoneWhatsapp,
        string Url);

    public sealed record ListingDetailDto(
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
        string CityName,
        string LocalitySlug,
        string LocalityName,
        string CategorySlug,
        string CategoryName,
        string Url);

    public static RouteGroupBuilder MapPublicDirectoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/cities", GetCities);
        group.MapGet("/cities/{citySlug}/localities", GetLocalities);
        group.MapGet("/categories", GetCategories);
        group.MapGet("/listings/nearby", GetNearby);
        group.MapGet("/listings/{citySlug}/{localitySlug}/{slug}", GetListing);
        return group;
    }

    private static async Task<IResult> GetCities(NpgsqlDataSource db, CancellationToken ct)
    {
        await using var command = db.CreateCommand("SELECT slug, name, state FROM cities WHERE is_active ORDER BY name");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var cities = new List<CityDto>();
        while (await reader.ReadAsync(ct))
        {
            cities.Add(new CityDto(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return Results.Ok(cities);
    }

    private static async Task<IResult> GetLocalities(string citySlug, NpgsqlDataSource db, CancellationToken ct)
    {
        await using var command = db.CreateCommand(
            @"SELECT l.slug, l.name, ST_Y(l.center::geometry), ST_X(l.center::geometry)
              FROM localities l
              JOIN cities c ON c.id = l.city_id
              WHERE c.slug = @city AND c.is_active AND l.is_active
              ORDER BY l.name");
        command.Parameters.AddWithValue("city", citySlug);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var localities = new List<LocalityDto>();
        while (await reader.ReadAsync(ct))
        {
            localities.Add(new LocalityDto(reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3)));
        }
        return localities.Count == 0 ? Results.NotFound() : Results.Ok(localities);
    }

    private static async Task<IResult> GetCategories(NpgsqlDataSource db, CancellationToken ct)
    {
        await using var command = db.CreateCommand(
            "SELECT id, parent_id, slug, name, search_terms FROM categories WHERE is_active ORDER BY level, sort_order, name");
        await using var reader = await command.ExecuteReaderAsync(ct);

        var parents = new List<(int Id, CategoryDto Dto)>();
        var childrenByParent = new Dictionary<int, List<SubCategoryDto>>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);
            var slug = reader.GetString(2);
            var name = reader.GetString(3);
            if (reader.IsDBNull(1))
            {
                var dto = new CategoryDto(slug, name, new List<SubCategoryDto>());
                parents.Add((id, dto));
                childrenByParent[id] = dto.Children;
            }
            else if (childrenByParent.TryGetValue(reader.GetInt32(1), out var siblings))
            {
                siblings.Add(new SubCategoryDto(slug, name, reader.GetFieldValue<string[]>(4)));
            }
        }
        return Results.Ok(parents.Select(p => p.Dto));
    }

    private static async Task<IResult> GetNearby(
        double lat,
        double lng,
        string? category,
        double? radiusKm,
        int? limit,
        NpgsqlDataSource db,
        CancellationToken ct)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            return Results.Problem("lat must be between -90 and 90, and lng between -180 and 180.", statusCode: StatusCodes.Status400BadRequest);
        }

        var radiusMetres = Math.Clamp(radiusKm ?? 10, 0.1, 50) * 1000;
        var max = Math.Clamp(limit ?? 20, 1, 50);

        await using var command = db.CreateCommand(
            @"SELECT li.slug, li.name, ci.slug, lo.slug, lo.name, pc.slug, pc.name,
                     ST_Distance(li.location, p.pt), li.address_line, li.phone_primary, li.phone_whatsapp
              FROM (SELECT ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography AS pt) p
              JOIN listings li ON li.status = 'active' AND ST_DWithin(li.location, p.pt, @radius)
              JOIN localities lo ON lo.id = li.locality_id
              JOIN cities ci ON ci.id = lo.city_id
              JOIN listing_categories lc ON lc.listing_id = li.id AND lc.is_primary
              JOIN categories pc ON pc.id = lc.category_id
              WHERE (@cat::text IS NULL OR EXISTS (
                        SELECT 1
                        FROM listing_categories x
                        JOIN categories xc ON xc.id = x.category_id
                        LEFT JOIN categories xp ON xp.id = xc.parent_id
                        WHERE x.listing_id = li.id AND (xc.slug = @cat OR xp.slug = @cat)))
              ORDER BY li.location <-> p.pt
              LIMIT @max");
        command.Parameters.AddWithValue("lat", lat);
        command.Parameters.AddWithValue("lng", lng);
        command.Parameters.AddWithValue("radius", radiusMetres);
        command.Parameters.Add(new NpgsqlParameter("cat", NpgsqlDbType.Text) { Value = (object?)category ?? DBNull.Value });
        command.Parameters.AddWithValue("max", max);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<NearbyListingDto>();
        while (await reader.ReadAsync(ct))
        {
            var slug = reader.GetString(0);
            var citySlug = reader.GetString(2);
            var localitySlug = reader.GetString(3);
            var categorySlug = reader.GetString(5);
            results.Add(new NearbyListingDto(
                slug,
                reader.GetString(1),
                citySlug,
                localitySlug,
                reader.GetString(4),
                categorySlug,
                reader.GetString(6),
                (int)Math.Round(reader.GetDouble(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                $"/{citySlug}/{localitySlug}/{categorySlug}/{slug}"));
        }
        return Results.Ok(results);
    }

    private static async Task<IResult> GetListing(string citySlug, string localitySlug, string slug, NpgsqlDataSource db, CancellationToken ct)
    {
        await using var command = db.CreateCommand(
            @"SELECT li.slug, li.name, li.description, li.address_line, li.pincode, li.phone_primary, li.phone_whatsapp,
                     li.website_url, ST_Y(li.location::geometry), ST_X(li.location::geometry),
                     ci.slug, ci.name, lo.slug, lo.name, pc.slug, pc.name
              FROM listings li
              JOIN localities lo ON lo.id = li.locality_id
              JOIN cities ci ON ci.id = lo.city_id
              JOIN listing_categories lc ON lc.listing_id = li.id AND lc.is_primary
              JOIN categories pc ON pc.id = lc.category_id
              WHERE ci.slug = @city AND lo.slug = @locality AND li.slug = @slug AND li.status = 'active'");
        command.Parameters.AddWithValue("city", citySlug);
        command.Parameters.AddWithValue("locality", localitySlug);
        command.Parameters.AddWithValue("slug", slug);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return Results.NotFound();
        }

        var categorySlug = reader.GetString(14);
        return Results.Ok(new ListingDetailDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetDouble(8),
            reader.GetDouble(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            categorySlug,
            reader.GetString(15),
            $"/{reader.GetString(10)}/{reader.GetString(12)}/{categorySlug}/{reader.GetString(0)}"));
    }
}
