namespace BusinessDirectory.Api.Listings;

/// <summary>What a reviewer submits. Untrusted until validated.</summary>
public sealed record ReviewInput(int? Rating, string? Body);

public sealed record ValidReview(short Rating, string Body);

public sealed record ReviewValidationResult(ValidReview? Value, Dictionary<string, string[]> Errors);

/// <summary>
/// Rules for a customer review, kept out of the endpoint so they can be unit-tested without a
/// database. They mirror the CHECK constraints in V0007.
///
/// The minimum body length is deliberate: a one-word review carries no information for the next
/// customer and is the easiest kind to mass-produce.
/// </summary>
public static class ReviewValidator
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MinBodyLength = 20;
    public const int MaxBodyLength = 2000;

    public static ReviewValidationResult Validate(ReviewInput input)
    {
        var errors = new Dictionary<string, string[]>();

        if (input.Rating is not { } rating || rating < MinRating || rating > MaxRating)
        {
            errors["rating"] = [$"Choose a rating from {MinRating} to {MaxRating} stars."];
        }

        var body = input.Body?.Trim();
        if (string.IsNullOrEmpty(body) || body.Length < MinBodyLength)
        {
            errors["body"] = [$"Tell other customers what your experience was like, in at least {MinBodyLength} characters."];
        }
        else if (body.Length > MaxBodyLength)
        {
            errors["body"] = [$"Review must be {MaxBodyLength} characters or fewer."];
        }

        return errors.Count > 0
            ? new ReviewValidationResult(null, errors)
            : new ReviewValidationResult(new ValidReview((short)input.Rating!.Value, body!), errors);
    }
}
