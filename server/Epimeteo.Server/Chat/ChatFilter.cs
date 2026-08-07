using System.Text;
using System.Text.RegularExpressions;

namespace Epimeteo.Server.Chat;

/// <summary>
/// Censura básica antes de retransmitir (FASE-11 §1, a propósito deliberadamente simple: una
/// lista fija, no un servicio de moderación). <c>chat_log</c> guarda el texto sin censurar —
/// esto sólo toca lo que le llega a los demás jugadores (FASE-11 §2 D9).
/// </summary>
public static class ChatFilter
{
    private static readonly string[] Blocked = ["gilipollas", "imbécil", "mierda", "puta", "puto"];

    private static readonly Regex[] Patterns = Blocked
        .Select(word => new Regex($@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    /// <summary>Sustituye cada aparición de una palabra bloqueada por asteriscos de su mismo tamaño.</summary>
    public static string Censor(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var pattern in Patterns)
        {
            text = pattern.Replace(text, match => new string('*', match.Length));
        }

        return text;
    }
}
