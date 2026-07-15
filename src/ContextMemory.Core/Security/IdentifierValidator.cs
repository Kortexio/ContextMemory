using System.Text.RegularExpressions;

namespace ContextMemory.Core.Security;

public static partial class IdentifierValidator
{
    private const int MaxLength = 64;

    [GeneratedRegex("^[a-zA-Z0-9-]+$")]
    private static partial Regex ValidPattern();

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxLength
        && ValidPattern().IsMatch(value);
}
