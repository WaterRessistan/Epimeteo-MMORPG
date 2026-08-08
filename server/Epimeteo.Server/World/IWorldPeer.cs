using Epimeteo.Server.Security;
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

    /// <summary>
    /// Apunta una anomalía a esta sesión (FASE-13 §2 D4). El mundo sólo <b>informa</b>: cuándo eso
    /// deja de ser ruido y qué hacer al respecto —contar, avisar o desconectar— lo decide la
    /// sesión con su propio <c>AnomalyRecorder</c>, que es quien tiene la ventana y la IP.
    /// </summary>
    void RecordAnomaly(AnomalyKind kind);
}
