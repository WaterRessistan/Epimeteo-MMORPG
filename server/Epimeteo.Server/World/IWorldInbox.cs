using Epimeteo.Shared.Net;

namespace Epimeteo.Server.World;

/// <summary>
/// Punto de entrega de todo lo que toca estado de mundo. El bucle de red lo deposita desde su
/// hilo async y el bucle de simulación lo drena al principio de cada tick: es la única frontera
/// entre ambos hilos.
/// </summary>
public interface IWorldInbox
{
    /// <summary>
    /// Encola un mensaje de cliente para el próximo tick. La implementación <b>copia</b> el
    /// payload: el buffer del bucle de lectura se reutiliza en cuanto esta llamada retorna.
    /// </summary>
    void Post(int sessionId, Opcode opcode, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Encola una orden de control (entrar al mundo o salir de él). Va por una cola aparte porque
    /// se drena <b>antes</b> que los mensajes: un <c>InputState</c> nunca debe llegar a la
    /// simulación antes que el <c>join</c> del jugador que lo manda.
    /// </summary>
    void PostControl(WorldCommand command);
}
