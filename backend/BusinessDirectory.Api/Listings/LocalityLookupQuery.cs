namespace BusinessDirectory.Api.Listings;

/// <summary>
/// Escapes characters that have special meaning in SQL LIKE patterns.
/// The SQL Server query must use ESCAPE '\' so the backslash escapes
/// literal percent, underscore, and backslash characters.
/// </summary>
public sealed record LocalitySearchQuery(string Text, string? CitySlug, int Limit)
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 25;

    /// <summary>One letter would match most of the city, which is no help to anyone typing.</summary>
    public const int MinTextLength = 2;

    /// <summary>Longer text is cut rather than rejected: it is almost always a stray paste.</summary>
    public const int MaxTextLength = 60;

    /// <summary>LIKE pattern for "starts with what was typed" - the matches worth showing first.</summary>
    public string PrefixPattern => EscapeForLike(Text) + "%";

    /// <summary>LIKE pattern for "contains what was typed".</summary>
    public string ContainsPattern => "%" + EscapeForLike(Text) + "%";

    public static LocalitySearchQuery? TryCreate(string? q, string? city, int? limit, out string? error)
    {
        error = null;

        var text = Trim(q);
        if (text is null || text.Length < MinTextLength)
        {
            error = $"q must be at least {MinTextLength} characters.";
            return null;
        }

        if (text.Length > MaxTextLength)
        {
            text = text[..MaxTextLength];
        }

        return new LocalitySearchQuery(text, Trim(city), Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit));
    }

    /// <summary>
/// Escapes characters that have special meaning in SQL LIKE patterns.
/// The corresponding SQL Server query uses ESCAPE '\' so backslashes
/// can escape literal percent, underscore, and backslash characters.
/// </summary>
    private static string EscapeForLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Validated coordinates for GET /api/v1/public/localities/nearest. Both values are required and
/// must be real world coordinates, matching how <see cref="ListingSearchQuery"/> checks them.
/// </summary>
public sealed record NearestLocalityQuery(double Latitude, double Longitude)
{
    public static NearestLocalityQuery? TryCreate(double? lat, double? lng, out string? error)
    {
        error = null;

        if (!lat.HasValue || !lng.HasValue)
        {
            error = "lat and lng must be supplied together.";
            return null;
        }

        if (lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            error = "lat must be between -90 and 90, and lng between -180 and 180.";
            return null;
        }

        return new NearestLocalityQuery(lat.Value, lng.Value);
    }
}
