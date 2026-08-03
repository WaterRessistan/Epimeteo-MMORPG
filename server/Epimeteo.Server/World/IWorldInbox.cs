using Epimeteo.Shared.Net;

namespace Epimeteo.Server.World;

/// <summary>
/// Punto de entrega de los mensajes que sí tocan estado de mundo. El bucle de red los deposita
/// desde su hilo async y el bucle de simulación los drena al principio de cada tick: es la única
/// frontera entre ambos hilos.
/// <para>
/// En la Fase 1 no hay mundo y ningún opcode llega hasta aquí, pero la frontera se fija ya para
/// que la Fase 4 no tenga que reinventarla.
/// </para>
/// </summary>
public interface IWorldInbox
{
    /// <summary>
    /// Encola un mensaje para el próximo tick. La implementación <b>copia</b> el payload:
    /// el buffer del bucle de lectura se reutiliza en cuanto esta llamada retorna.
    /// </summary>
    void Post(int sessionId, Opcode opcode, ReadOnlySpan<byte> payload);
}
