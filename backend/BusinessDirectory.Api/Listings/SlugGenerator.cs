using System.Text;

namespace BusinessDirectory.Api.Listings;

/// <summary>Builds URL slugs for listings: lowercase letters, digits and single hyphens.</summary>
public static class SlugGenerator
{
    public const int MaxLength = 60;

    public static string FromName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastWasHyphen = true;   // suppresses a leading hyphen
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].Trim('-');
        }
        return slug.Length == 0 ? "business" : slug;
    }

    /// <summary>Returns the base slug if free, otherwise base-2, base-3 ... (slugs are unique per area).</summary>
    public static string NextFree(string baseSlug, ISet<string> taken)
    {
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{baseSlug}-{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
