using System.Text.RegularExpressions;

namespace BusinessDirectory.Api.Notifications;

/// <summary>
/// Whether an address is worth trying to send to.
///
/// This is deliberately not a full RFC 5322 parser. The addresses it sees come from our own
/// database, typed by an owner during onboarding, and the only question here is whether handing it
/// to a provider is worth the attempt — an empty string, a stray name, or a header-injection
/// attempt should never get that far. The pattern matches the one in
/// <see cref="Listings.EnquiryValidator"/> and the CHECK constraint in V0006, so the three agree.
/// </summary>
public static partial class EmailAddress
{
    /// <summary>The limit in the RFC, and the one the enquiries and users columns are sized for.</summary>
    public const int MaxLength = 254;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex Pattern();

    /// <summary>
    /// Trims and lowercases, and answers false for anything we would not send to. A newline or a
    /// carriage return fails on the whitespace rule, which is what keeps a stored address from
    /// smuggling an extra header into a provider's API call.
    /// </summary>
    public static bool TryNormalize(string? candidate, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim().ToLowerInvariant();
        if (trimmed.Length > MaxLength || !Pattern().IsMatch(trimmed))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    public static bool IsValid(string? candidate) => TryNormalize(candidate, out _);
}
