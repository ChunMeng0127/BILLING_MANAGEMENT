using System.Text;

namespace BillingControl.Services;

/// <summary>
/// Produces stable, human-derived internal keys for the staff-facing checklist workflow.
/// It deliberately performs no transliteration or country/domain-specific interpretation.
/// </summary>
public static class DocumentChecklistKeyGenerator
{
    public const int MaxLength = 100;

    public static string Slugify(string? value, string fallback)
    {
        var builder = new StringBuilder();
        var separatorPending = false;

        foreach (var character in value?.Trim().ToLowerInvariant() ?? string.Empty)
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                if (separatorPending && builder.Length > 0)
                    builder.Append('-');

                builder.Append(character);
                separatorPending = false;
                continue;
            }

            if (character is ' ' or '\t' or '\r' or '\n' or '-' or '_')
                separatorPending = true;
        }

        var result = builder.ToString().Trim('-');
        if (result.Length == 0)
            result = fallback;

        if (result.Length > MaxLength)
            result = result[..MaxLength].TrimEnd('-');

        return result.Length == 0 ? "checklist" : result;
    }

    public static string AllocateUnique(string baseKey, IEnumerable<string> existingKeys)
    {
        var used = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = Slugify(baseKey, "checklist");
        if (!used.Contains(candidate))
            return candidate;

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var suffixText = $"-{suffix}";
            var prefixLength = Math.Max(1, MaxLength - suffixText.Length);
            var prefix = candidate.Length > prefixLength
                ? candidate[..prefixLength].TrimEnd('-')
                : candidate;
            var suffixed = $"{prefix}{suffixText}";
            if (!used.Contains(suffixed))
                return suffixed;
        }

        throw new InvalidOperationException("No unique checklist key could be allocated.");
    }
}
