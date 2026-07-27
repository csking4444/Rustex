namespace Rustex.Domain.RustPlus;

public enum TeamChatCommand
{
    Help,
    Pop,
    Time,
    Team,
    Alerts,
    Wipe,
    Pos,
    Device,
    Unknown,
}

public sealed record ParsedTeamChatCommand(TeamChatCommand Command, string? Argument);

/// <summary>Parses team chat messages into bot commands. Framework-free and pure — the responder
/// (which actually calls TemplateRenderer/SendTeamMessageAsync) is a separate, thin layer over
/// this, kept apart specifically so the parsing rules are unit-testable without a live
/// connection or a database.</summary>
public static class TeamChatCommandParser
{
    /// <summary>Returns null for anything that isn't a command the bot should respond to —
    /// including, deliberately, any message from <paramref name="botSteamId"/> itself. That
    /// check lives here rather than in the responder because it's the one loop-guard that must
    /// never be skipped: without it, the bot would parse and reply to its own messages forever.</summary>
    public static ParsedTeamChatCommand? TryParse(string message, ulong senderSteamId, ulong botSteamId)
    {
        if (senderSteamId == botSteamId) return null;

        var trimmed = message.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '!') return null;

        var spaceIndex = trimmed.IndexOf(' ');
        var word = (spaceIndex < 0 ? trimmed : trimmed[..spaceIndex]).ToLowerInvariant();
        var argument = spaceIndex < 0 ? null : trimmed[(spaceIndex + 1)..].Trim();
        if (argument is { Length: 0 }) argument = null;

        var command = word switch
        {
            "!help" => TeamChatCommand.Help,
            "!pop" => TeamChatCommand.Pop,
            "!time" => TeamChatCommand.Time,
            "!team" => TeamChatCommand.Team,
            "!alerts" => TeamChatCommand.Alerts,
            "!wipe" => TeamChatCommand.Wipe,
            "!pos" => TeamChatCommand.Pos,
            "!device" => TeamChatCommand.Device,
            _ => TeamChatCommand.Unknown,
        };

        return new ParsedTeamChatCommand(command, argument);
    }
}
