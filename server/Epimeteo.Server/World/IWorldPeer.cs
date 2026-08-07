using Epimeteo.Shared.Net;

namespace Epimeteo.Server.World;

/// <summary>
/// Lo único que el mundo necesita de una sesión: mandarle frames y poder echarla. Lo implementa
/// <c>Net.Session</c>, cuyo <c>Send</c> ya es seguro desde el hilo del tick porque escribe en un
/// <see cref="System.Threading.Channels.Channel{T}"/>.
/// <para>
/// Existe como interfaz para que los tests del mundo (AOI, snapshots) no tengan que levantar un
/// WebSocket: la simulación no debería saber qué es un socket.
/// </para>
/// </summary>
public interface IWorldPeer
{
    /// <summary>Id de la sesión, el mismo que llega en <see cref="WorldMessage.SessionId"/>.</summary>
    int Id { get; }

    /// <summary>
    /// RTT medido por el servidor, en ms (FASE-09 §2 D1). Lo usa la compensación de latencia para
    /// decidir cuánto rebobinar, así que es medido, no declarado por el cliente. 0 mientras no
    /// haya llegado ningún <c>Ping</c> con eco.
    /// </summary>
    int RttMs { get; }

    /// <summary>Encola un mensaje hacia el cliente.</summary>
    void Send<T>(Opcode opcode, T payload);

    /// <summary>Cierra la sesión.</summary>
    void Kick(KickReason reason, ResultCode detail = ResultCode.Ok);
}
