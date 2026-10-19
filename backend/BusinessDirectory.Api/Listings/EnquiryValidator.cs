using System.Text.RegularExpressions;
using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.Api.Listings;

/// <summary>What a member of the public sends when contacting a business. Untrusted until validated.</summary>
public sealed record EnquiryInput(string? Name, string? Phone, string? Email, string? Message);

/// <summary>The cleaned version. The phone is normalised to E.164 so it matches the CHECK constraint.</summary>
public sealed record ValidEnquiry(string Name, string PhoneE164, string? Email, string Message);

public sealed record EnquiryValidationResult(ValidEnquiry? Value, Dictionary<string, string[]> Errors);

/// <summary>
/// Rules for a customer enquiry, kept out of the endpoint so they can be unit-tested without a
/// database. They mirror the CHECK constraints in V0006 — the database is the backstop, this is the
/// part that can explain itself to the person filling in the form.
/// </summary>
public static partial class EnquiryValidator
{
    public const int MinMessageLength = 10;
    public const int MaxMessageLength = 2000;
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailPattern();

    public static EnquiryValidationResult Validate(EnquiryInput input)
    {
        var errors = new Dictionary<string, string[]>();

        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = ["Please enter your name."];
        }
        else if (name.Length > MaxNameLength)
        {
            errors["name"] = [$"Name must be {MaxNameLength} characters or fewer."];
        }

        // The same normalisation the owner's own sign-in uses, so an Indian mobile typed any of the
        // usual ways becomes the one form the database stores.
        string? phoneE164 = null;
        if (string.IsNullOrWhiteSpace(input.Phone))
        {
            errors["phone"] = ["Please enter your mobile number."];
        }
        else if (!PhoneNumber.TryNormalizeIndia(input.Phone, out phoneE164))
        {
            errors["phone"] = ["Enter a 10-digit Indian mobile number."];
        }

        // Optional: a phone number is enough to reply to.
        var email = input.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
        {
            email = null;
        }
        else if (email.Length > MaxEmailLength || !EmailPattern().IsMatch(email))
        {
            errors["email"] = ["That does not look like a valid email address."];
        }

        var message = input.Message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length < MinMessageLength)
        {
            errors["message"] = [$"Tell the business what you need, in at least {MinMessageLength} characters."];
        }
        else if (message.Length > MaxMessageLength)
        {
            errors["message"] = [$"Message must be {MaxMessageLength} characters or fewer."];
        }

        return errors.Count > 0
            ? new EnquiryValidationResult(null, errors)
            : new EnquiryValidationResult(new ValidEnquiry(name!, phoneE164!, email, message!), errors);
    }
}
