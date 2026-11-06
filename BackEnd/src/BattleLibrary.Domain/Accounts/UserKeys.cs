using System.Text;

namespace BattleLibrary.Domain.Accounts;

// Shared matching rules for username/email lookups, so every endpoint agrees.
public static class UserKeys
{
    // Trimmed, NFKC-normalized, leading "@" removed, lower-cased. Compare against lower(column).
    public static string Username(string? input)
    {
        var s = (input ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
        if (s.StartsWith('@'))
        {
            s = s[1..].TrimStart();
        }
        return s.ToLowerInvariant();
    }

    public static string Email(string? input) =>
        (input ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    // Escapes LIKE wildcards so user input matches literally (Npgsql default escape is "\").
    public static string EscapeLike(string term) =>
        term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
