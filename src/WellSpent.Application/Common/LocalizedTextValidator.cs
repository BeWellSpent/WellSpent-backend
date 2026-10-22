using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Common;

/// <summary>Validates a locale-keyed text map (issue #89): "en" required, every entry capped and trimmed.</summary>
public static class LocalizedTextValidator
{
    public static Dictionary<string, string> ValidateAndTrim(Dictionary<string, string> input, int maxLength, string fieldName)
    {
        var result = new Dictionary<string, string>();
        foreach (var (locale, value) in input)
        {
            var trimmed = value.Trim();
            // Counted in Unicode codepoints, not UTF-16 code units — matches Go's utf8.RuneCountInString.
            if (trimmed.EnumerateRunes().Count() > maxLength)
            {
                throw new AppValidationException($"{fieldName}['{locale}'] must be {maxLength} characters or fewer");
            }
            result[locale] = trimmed;
        }
        if (!result.TryGetValue("en", out var en) || en.Length == 0)
        {
            throw new AppValidationException($"{fieldName}['en'] is required");
        }
        return result;
    }
}
