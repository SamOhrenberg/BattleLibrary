using BattleLibrary.Domain.Common;

namespace BattleLibrary.Domain.OAuth;

// A public OAuth client (an MCP host) registered via dynamic client registration.
// Its Id, formatted "N", is the client_id.
public class OAuthClient : Entity
{
    public string ClientName { get; set; } = string.Empty;
    public List<string> RedirectUris { get; set; } = [];

    public string PublicId => Id.ToString("N");
}
