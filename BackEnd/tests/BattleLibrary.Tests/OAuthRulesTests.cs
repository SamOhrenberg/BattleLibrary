using BattleLibrary.Domain.OAuth;
using Xunit;

namespace BattleLibrary.Tests;

public class OAuthRulesTests
{
    // RFC 7636 appendix B test vector.
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public void Pkce_accepts_the_rfc_vector() => Assert.True(OAuthRules.VerifyPkce(Verifier, Challenge));

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXX")]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjX!")]
    public void Pkce_rejects_bad_verifiers(string? verifier) => Assert.False(OAuthRules.VerifyPkce(verifier, Challenge));

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("http://localhost:33418/callback", true)]
    [InlineData("http://127.0.0.1/cb", true)]
    [InlineData("cursor://anysphere.cursor-retrieval/oauth/callback", true)]
    [InlineData("http://evil.example/cb", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("https://claude.ai/cb#frag", false)]
    [InlineData("/relative", false)]
    [InlineData("", false)]
    public void Redirect_uri_policy(string uri, bool allowed) => Assert.Equal(allowed, OAuthRules.IsAllowedRedirectUri(uri));

    [Fact]
    public void Redirect_match_is_exact_for_https()
    {
        string[] registered = ["https://claude.ai/api/mcp/auth_callback"];
        Assert.True(OAuthRules.RedirectMatches(registered, "https://claude.ai/api/mcp/auth_callback"));
        Assert.False(OAuthRules.RedirectMatches(registered, "https://claude.ai/api/mcp/auth_callback/"));
        Assert.False(OAuthRules.RedirectMatches(registered, "https://claude.ai.evil.example/api/mcp/auth_callback"));
        Assert.False(OAuthRules.RedirectMatches(registered, null));
    }

    [Fact]
    public void Redirect_match_ignores_port_on_loopback_only()
    {
        string[] registered = ["http://localhost:1000/callback", "https://example.com:8443/cb"];
        Assert.True(OAuthRules.RedirectMatches(registered, "http://localhost:54321/callback"));
        Assert.False(OAuthRules.RedirectMatches(registered, "http://localhost:54321/other"));
        Assert.False(OAuthRules.RedirectMatches(registered, "http://127.0.0.1:54321/callback"));
        Assert.False(OAuthRules.RedirectMatches(registered, "https://example.com:9443/cb"));
    }

    [Fact]
    public void Append_query_keeps_existing_query_and_skips_nulls()
    {
        var result = OAuthRules.AppendQuery("https://a.example/cb?x=1",
            [new("code", "a b"), new("state", null), new("iss", "https://api.example")]);
        Assert.Equal("https://a.example/cb?x=1&code=a%20b&iss=https%3A%2F%2Fapi.example", result);
    }

    [Fact]
    public void Challenge_shape_is_checked()
    {
        Assert.True(OAuthRules.IsValidChallenge(Challenge));
        Assert.False(OAuthRules.IsValidChallenge(Challenge + "A"));
        Assert.False(OAuthRules.IsValidChallenge(null));
    }
}
