using System.Text.RegularExpressions;

namespace BusinessDirectory.Api.Auth;

/// <summary>Phone number normalisation. Launch market is India, so only Indian mobile numbers are accepted for now.</summary>
public static partial class PhoneNumber
{
    [GeneratedRegex("^[6-9][0-9]{9}$")]
    private static partial Regex IndianMobile();

    /// <summary>
    /// Accepts 9876543210, 09876543210, 919876543210, +91 98765 43210, +91-98765-43210 and returns +919876543210.
    /// Returns false for anything else (landlines, other countries, letters, too long).
    /// </summary>
    public static bool TryNormalizeIndia(string? input, out string e164)
    {
        e164 = "";
        if (string.IsNullOrWhiteSpace(input) || input.Length > 25)
        {
            return false;
        }

        var digits = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input.Trim())
        {
            if (ch is >= '0' and <= '9')
            {
                digits.Append(ch);
            }
            else if (ch is ' ' or '-' or '(' or ')' || (ch == '+' && digits.Length == 0))
            {
                // allowed formatting characters
            }
            else
            {
                return false;
            }
        }

        var number = digits.ToString();
        if (number.Length == 12 && number.StartsWith("91", StringComparison.Ordinal))
        {
            number = number[2..];
        }
        else if (number.Length == 11 && number.StartsWith('0'))
        {
            number = number[1..];
        }

        if (!IndianMobile().IsMatch(number))
        {
            return false;
        }

        e164 = "+91" + number;
        return true;
    }
}
