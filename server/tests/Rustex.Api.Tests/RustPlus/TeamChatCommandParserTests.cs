using Rustex.Domain.RustPlus;
using Xunit;

namespace Rustex.Api.Tests.RustPlus;

public class TeamChatCommandParserTests
{
    private const ulong BotSteamId = 76561198000000099;
    private const ulong PlayerSteamId = 76561198000000001;

    [Fact]
    public void Pop_ParsesAsPopWithNoArgument()
    {
        var result = TeamChatCommandParser.TryParse("!pop", PlayerSteamId, BotSteamId);

        Assert.NotNull(result);
        Assert.Equal(TeamChatCommand.Pop, result.Command);
        Assert.Null(result.Argument);
    }

    [Fact]
    public void PosWithArgument_ParsesCommandAndArgument()
    {
        var result = TeamChatCommandParser.TryParse("!pos Bob", PlayerSteamId, BotSteamId);

        Assert.NotNull(result);
        Assert.Equal(TeamChatCommand.Pos, result.Command);
        Assert.Equal("Bob", result.Argument);
    }

    [Fact]
    public void UnknownBangCommand_ParsesAsUnknown_NotIgnored()
    {
        var result = TeamChatCommandParser.TryParse("!doesnotexist", PlayerSteamId, BotSteamId);

        Assert.NotNull(result);
        Assert.Equal(TeamChatCommand.Unknown, result.Command);
    }

    [Fact]
    public void OrdinaryChatter_WithoutBangPrefix_IsIgnored()
    {
        Assert.Null(TeamChatCommandParser.TryParse("heading to launch, anyone free?", PlayerSteamId, BotSteamId));
    }

    [Fact]
    public void MessageFromBotItself_IsIgnored()
    {
        // The one loop-guard that must never be skipped — without it the bot replies to its own
        // messages forever.
        Assert.Null(TeamChatCommandParser.TryParse("!pop", BotSteamId, BotSteamId));
    }

    [Fact]
    public void LeadingAndTrailingWhitespace_IsTrimmed()
    {
        var result = TeamChatCommandParser.TryParse("   !pop   ", PlayerSteamId, BotSteamId);

        Assert.NotNull(result);
        Assert.Equal(TeamChatCommand.Pop, result.Command);
    }

    [Fact]
    public void ArgumentWhitespace_IsTrimmedAndEmptyBecomesNull()
    {
        var result = TeamChatCommandParser.TryParse("!pos   ", PlayerSteamId, BotSteamId);

        Assert.NotNull(result);
        Assert.Equal(TeamChatCommand.Pos, result.Command);
        Assert.Null(result.Argument);
    }

    [Theory]
    [InlineData("!POP")]
    [InlineData("!Pop")]
    [InlineData("!pOp")]
    public void CommandWord_IsCaseInsensitive(string message)
    {
        var result = TeamChatCommandParser.TryParse(message, PlayerSteamId, BotSteamId);

        Assert.NotNull(result);
        Assert.Equal(TeamChatCommand.Pop, result.Command);
    }

    [Fact]
    public void ArgumentCasing_IsPreservedAsTyped()
    {
        var result = TeamChatCommandParser.TryParse("!pos BOB", PlayerSteamId, BotSteamId);

        Assert.Equal("BOB", result!.Argument);
    }

    [Fact]
    public void EmptyMessage_IsIgnored()
    {
        Assert.Null(TeamChatCommandParser.TryParse("", PlayerSteamId, BotSteamId));
        Assert.Null(TeamChatCommandParser.TryParse("   ", PlayerSteamId, BotSteamId));
    }
}
