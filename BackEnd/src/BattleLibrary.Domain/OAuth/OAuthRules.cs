using System.Security.Cryptography;
using System.Text;

namespace BattleLibrary.Domain.OAuth;

// Pure OAuth 2.1 checks shared by the endpoints and tests.
public static class OAuthRules
{
    public const string McpReadScope = "mcp:read";

    private static readonly HashSet<string> BlockedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "data", "file", "vbscript", "about", "blob", "ws", "wss", "ftp",
    };

    // https anywhere, http only on loopback, or a native app's private-use scheme.
    public static bool IsAllowedRedirectUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            return IsLoopback(uri);
        }
        return !BlockedSchemes.Contains(uri.Scheme);
    }

    // Exact match, except loopback redirects may use any port (RFC 8252 7.3).
    public static bool RedirectMatches(IEnumerable<string> registered, string? requested)
    {
        if (string.IsNullOrEmpty(requested) || !Uri.TryCreate(requested, UriKind.Absolute, out var req))
        {
            return false;
        }
        foreach (var candidate in registered)
        {
            if (string.Equals(candidate, requested, StringComparison.Ordinal))
            {
                return true;
            }
            if (IsLoopback(req) && Uri.TryCreate(candidate, UriKind.Absolute, out var reg) && IsLoopback(reg)
                && reg.Scheme == req.Scheme
                && string.Equals(reg.Host, req.Host, StringComparison.OrdinalIgnoreCase)
                && reg.AbsolutePath == req.AbsolutePath
                && reg.Query == req.Query)
            {
                return true;
            }
        }
        return false;
    }

    // RFC 7636: verifier is 43-128 unreserved chars; S256 challenge is its base64url SHA-256.
    public static bool VerifyPkce(string? verifier, string challenge)
    {
        if (verifier is null || verifier.Length is < 43 or > 128 || !verifier.All(IsUnreserved))
        {
            return false;
        }
        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    public static bool IsValidChallenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Appends query parameters to a redirect URI that may already carry a query.
    public static string AppendQuery(string uri, IEnumerable<KeyValuePair<string, string?>> values)
    {
        var parts = values
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        var query = string.Join('&', parts);
        if (query.Length == 0)
        {
            return uri;
        }
        return uri.Contains('?') ? $"{uri}&{query}" : $"{uri}?{query}";
    }

    private static bool IsLoopback(Uri uri) =>
        uri.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";

    private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
