using FCP.Core.Accounts;

namespace FCP.Core.Tests.Accounts;

public class OfflineNameTests
{
    [Theory]
    [InlineData("Steve")]
    [InlineData("_player_01")]
    [InlineData("方块人")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    public void ValidNames(string name) => AccountService.ValidateOfflineName(name).ShouldBeNull();

    [Theory]
    [InlineData("", "请输入")]
    [InlineData("   ", "请输入")]
    [InlineData("ABCDEFGHIJKLMNOPQ", "16")]
    [InlineData("a b", "只能包含")]
    [InlineData("name!", "只能包含")]
    [InlineData("名字😀", "只能包含")]
    public void InvalidNames(string name, string expected) => AccountService.ValidateOfflineName(name).ShouldNotBeNull().ShouldContain(expected);
}
