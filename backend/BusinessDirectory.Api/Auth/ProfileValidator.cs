using System.Text.RegularExpressions;

namespace BusinessDirectory.Api.Auth;

/// <summary>The owner's own details, as typed during onboarding. Untrusted until validated.</summary>
public sealed record ProfileInput(string? Name, string? Email);

/// <summary>The cleaned version of <see cref="ProfileInput"/>.</summary>
public sealed record ValidProfile(string Name, string Email);

public sealed record ProfileValidationResult(ValidProfile? Value, Dictionary<string, string[]> Errors);

/// <summary>
/// Rules for the name and email collected at onboarding. Kept apart from the endpoint so they can be
/// unit-tested without a database. The phone number is never validated here: it comes from the
/// signed-in token, not from the browser.
/// </summary>
public static partial class ProfileValidator
{
    public const int MaxNameLength = 100;
    public const int MinNameLength = 2;
    public const int MaxEmailLength = 254;

    // A letter, then letters, combining marks, spaces, full stops, apostrophes and hyphens.
    // \p{M} matters: in Telugu, Hindi and other Indic scripts the vowel signs are combining marks
    // rather than letters, so without it a name like "రమేష్" would be rejected. Digits and other
    // symbols stay out, since in a name they almost always signal a pasted mistake.
    [GeneratedRegex(@"^\p{L}[\p{L}\p{M} .'\-]*$")]
    private static partial Regex NamePattern();

    // Deliberately loose: something, an @, something, a dot, a couple more characters. Proving an
    // address really exists needs a confirmation email, not a stricter regex.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailPattern();

    public static ProfileValidationResult Validate(ProfileInput input)
    {
        var errors = new Dictionary<string, string[]>();

        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = ["Please enter your name."];
        }
        else if (name.Length is < MinNameLength or > MaxNameLength)
        {
            errors["name"] = [$"Name must be {MinNameLength} to {MaxNameLength} characters."];
        }
        else if (!NamePattern().IsMatch(name))
        {
            errors["name"] = ["Use letters, spaces and . ' - only."];
        }

        var email = input.Email?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            errors["email"] = ["Please enter your email address."];
        }
        else if (email.Length > MaxEmailLength)
        {
            errors["email"] = [$"Email must be {MaxEmailLength} characters or fewer."];
        }
        else if (!EmailPattern().IsMatch(email))
        {
            errors["email"] = ["That does not look like a valid email address."];
        }

        return errors.Count > 0
            ? new ProfileValidationResult(null, errors)
            : new ProfileValidationResult(new ValidProfile(name!, email!), errors);
    }
}
