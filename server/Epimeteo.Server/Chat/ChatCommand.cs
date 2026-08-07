using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Chat;

/// <summary>
/// Lo que pidió un <c>ChatSend</c>, ya interpretado (FASE-11 §2 D3). El cliente no sabe qué
/// comandos existen — sólo manda texto — así que todo el reconocimiento vive aquí, puro y
/// testeado, separado de <c>GameWorld</c> que es quien ejecuta cada uno.
/// </summary>
public abstract record ChatCommand
{
    private ChatCommand()
    {
    }

    /// <summary>Mensaje normal al canal elegido por el cliente.</summary>
    public sealed record Say(ChatChannel Channel, string Text) : ChatCommand;

    /// <summary><c>/w Nombre mensaje</c>.</summary>
    public sealed record Whisper(string TargetName, string Text) : ChatCommand;

    /// <summary><c>/who</c>: lista de conectados.</summary>
    public sealed record Who : ChatCommand;

    /// <summary><c>/help</c>: lista de comandos.</summary>
    public sealed record Help : ChatCommand;

    /// <summary><c>/kick Nombre [motivo]</c>. Sólo admins.</summary>
    public sealed record Kick(string TargetName, string Reason) : ChatCommand;

    /// <summary><c>/ban Nombre horas [motivo]</c>. Sólo admins.</summary>
    public sealed record Ban(string TargetName, int Hours, string Reason) : ChatCommand;

    /// <summary><c>/teleport Nombre</c>: mueve a quien lo manda junto al objetivo (FASE-11 §2 D8). Sólo admins.</summary>
    public sealed record Teleport(string TargetName) : ChatCommand;

    /// <summary><c>/give Nombre defKey cantidad</c>. Sólo admins.</summary>
    public sealed record Give(string TargetName, string DefKey, int Quantity) : ChatCommand;

    /// <summary>Un <c>/algo</c> que no se reconoce, o con argumentos que no cuadran.</summary>
    public sealed record Invalid(ResultCode Code) : ChatCommand;
}
