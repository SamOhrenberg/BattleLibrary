namespace BattleLibrary.Api.OAuth;

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    // Audience of MCP access tokens; the app's own API rejects them.
    public const string McpAudience = "battle-library-mcp";

    // The SPA's base URL, where the consent screen lives.
    public string FrontendUrl { get; set; } = string.Empty;

    // This API's public base URL. Empty uses the request's scheme and host.
    public string PublicUrl { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int CodeMinutes { get; set; } = 5;
}
