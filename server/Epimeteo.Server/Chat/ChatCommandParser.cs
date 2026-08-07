using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Chat;

/// <summary>
/// Reconoce un <c>ChatSend.Text</c> como comando de barra o mensaje normal. Puro: ni toca el
/// mundo ni sabe quién lo mandó — eso lo resuelve <c>GameWorld</c> con lo que devuelve aquí
/// (FASE-11 §2 D3).
/// </summary>
public static class ChatCommandParser
{
    public static ChatCommand Parse(ChatChannel channel, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return new ChatCommand.Invalid(ResultCode.InvalidCommand);
        }

        if (trimmed[0] != '/')
        {
            return new ChatCommand.Say(channel, trimmed);
        }

        var (command, rest) = SplitCommand(trimmed);

        return command.ToLowerInvariant() switch
        {
            "w" or "whisper" => ParseWhisper(rest),
            "who" => new ChatCommand.Who(),
            "help" => new ChatCommand.Help(),
            "kick" => ParseKick(rest),
            "ban" => ParseBan(rest),
            "tp" or "teleport" => ParseTeleport(rest),
            "give" => ParseGive(rest),
            _ => new ChatCommand.Invalid(ResultCode.InvalidCommand),
        };
    }

    private static (string Command, string Remainder) SplitCommand(string trimmed)
    {
        var spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex < 0
            ? (trimmed[1..], string.Empty)
            : (trimmed[1..spaceIndex], trimmed[(spaceIndex + 1)..].Trim());
    }

    private static ChatCommand ParseWhisper(string rest)
    {
        var spaceIndex = rest.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return new ChatCommand.Invalid(ResultCode.InvalidCommand);
        }

        var target = rest[..spaceIndex];
        var message = rest[(spaceIndex + 1)..].Trim();

        return target.Length == 0 || message.Length == 0
            ? new ChatCommand.Invalid(ResultCode.InvalidCommand)
            : new ChatCommand.Whisper(target, message);
    }

    private static ChatCommand ParseKick(string rest)
    {
        if (rest.Length == 0)
        {
            return new ChatCommand.Invalid(ResultCode.InvalidCommand);
        }

        var tokens = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var reason = tokens.Length == 2 ? tokens[1] : string.Empty;
        return new ChatCommand.Kick(tokens[0], reason);
    }

    private static ChatCommand ParseBan(string rest)
    {
        var tokens = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !int.TryParse(tokens[1], out var hours) || hours <= 0)
        {
            return new ChatCommand.Invalid(ResultCode.InvalidCommand);
        }

        var reason = tokens.Length == 3 ? tokens[2] : string.Empty;
        return new ChatCommand.Ban(tokens[0], hours, reason);
    }

    private static ChatCommand ParseTeleport(string rest)
    {
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 1
            ? new ChatCommand.Teleport(tokens[0])
            : new ChatCommand.Invalid(ResultCode.InvalidCommand);
    }

    private static ChatCommand ParseGive(string rest)
    {
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 3 || !int.TryParse(tokens[2], out var quantity) || quantity <= 0)
        {
            return new ChatCommand.Invalid(ResultCode.InvalidCommand);
        }

        return new ChatCommand.Give(tokens[0], tokens[1], quantity);
    }
}
