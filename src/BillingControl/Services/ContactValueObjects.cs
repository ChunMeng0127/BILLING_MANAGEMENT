using System.Text;

namespace BillingControl.Services;

/// <summary>
/// Validation and canonicalisation for Contact values. These helpers do not infer
/// country codes or make any authorization decision.
/// </summary>
public static class ContactValueObjects
{
    public static string NormalizeE164(string? value)
    {
        Finance.Require(!string.IsNullOrWhiteSpace(value), "An international phone number is required.");

        var candidate = value!.Trim();
        Finance.Require(candidate.StartsWith('+'),
            "Phone number must use an explicit international '+' prefix.");

        var digits = new StringBuilder(candidate.Length);
        foreach (var character in candidate[1..])
        {
            if (character is ' ' or '-' or '(' or ')')
                continue;

            Finance.Require(character is >= '0' and <= '9',
                "Phone number may contain only ASCII digits and spaces, hyphens, or parentheses after '+'.");
            digits.Append(character);
        }

        Finance.Require(digits.Length is >= 1 and <= 15,
            "Phone number must contain 1 to 15 digits after '+'.");
        Finance.Require(digits[0] is >= '1' and <= '9',
            "The first phone digit after '+' must be between 1 and 9.");

        return $"+{digits}";
    }

    public static string? NormalizePreferredLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim();
        Finance.Require(candidate.Length <= 35,
            "Preferred language must be 35 characters or fewer.");
        Finance.Require(!candidate.Contains('_') && !candidate.Any(char.IsWhiteSpace),
            "Preferred language must be a hyphen-separated language tag without whitespace or underscores.");

        var subtags = candidate.Split('-');
        Finance.Require(subtags.Length > 0 && subtags.All(x => x.Length > 0),
            "Preferred language contains an empty subtag.");
        Finance.Require(subtags.All(x => x.Length <= 8),
            "Preferred language subtags must be 8 characters or fewer.");
        Finance.Require(subtags.All(x => x.All(IsAsciiAlphaNumeric)),
            "Preferred language contains an invalid subtag.");
        Finance.Require(subtags[0].Length is >= 2 and <= 8 && subtags[0].All(IsAsciiLetter),
            "Preferred language must begin with a valid language subtag.");

        var normalized = new string[subtags.Length];
        normalized[0] = subtags[0].ToLowerInvariant();
        for (var index = 1; index < subtags.Length; index++)
        {
            var subtag = subtags[index];
            normalized[index] = subtag.Length == 4 && subtag.All(IsAsciiLetter)
                ? char.ToUpperInvariant(subtag[0]) + subtag[1..].ToLowerInvariant()
                : subtag.Length == 2 && subtag.All(IsAsciiLetter)
                    ? subtag.ToUpperInvariant()
                    : subtag.ToLowerInvariant();
        }

        var result = string.Join('-', normalized);
        Finance.Require(result.Length <= 35,
            "Preferred language must be 35 characters or fewer.");
        return result;
    }

    public static bool TryNormalizeE164(string? value, out string? normalized)
    {
        try
        {
            normalized = NormalizeE164(value);
            return true;
        }
        catch (BusinessException)
        {
            normalized = null;
            return false;
        }
    }

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiAlphaNumeric(char value) => IsAsciiLetter(value) || value is >= '0' and <= '9';
}
