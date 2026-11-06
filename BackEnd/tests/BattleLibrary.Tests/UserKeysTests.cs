using BattleLibrary.Domain.Accounts;
using Xunit;

namespace BattleLibrary.Tests;

public class UserKeysTests
{
    [Theory]
    [InlineData("MrLuemasG")]
    [InlineData("mrluemasg")]
    [InlineData("MRLUEMASG")]
    [InlineData(" mrluemasg ")]
    [InlineData("@mrluemasg")]
    [InlineData("@ MrLuemasG")]
    public void Username_variants_share_one_key(string input) =>
        Assert.Equal("mrluemasg", UserKeys.Username(input));

    [Fact]
    public void Username_applies_nfkc() =>
        Assert.Equal("bob", UserKeys.Username("ＢＯＢ"));

    [Fact]
    public void Email_is_trimmed_and_lowered() =>
        Assert.Equal("a@b.com", UserKeys.Email("  A@B.Com "));

    [Fact]
    public void EscapeLike_neutralizes_wildcards() =>
        Assert.Equal(@"a\%b\_c\\d", UserKeys.EscapeLike(@"a%b_c\d"));
}
