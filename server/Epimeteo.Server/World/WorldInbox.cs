using System.Collections.Concurrent;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.World;

/// <summary>Mensaje de cliente ya copiado y listo para procesarse dentro del tick.</summary>
/// <param name="SessionId">Sesión de origen.</param>
/// <param name="Opcode">Opcode ya validado contra <see cref="OpcodeTable"/>.</param>
/// <param name="Payload">Copia propia del payload MessagePack.</param>
public readonly record struct WorldMessage(int SessionId, Opcode Opcode, byte[] Payload);

/// <summary>
/// Implementación por defecto de <see cref="IWorldInbox"/>: una cola concurrente sin bloqueos
/// entre el hilo de red (productor) y el de simulación (consumidor único).
/// </summary>
public sealed class WorldInbox : IWorldInbox
{
    private readonly ConcurrentQueue<WorldMessage> _queue = new();

    /// <summary>Mensajes pendientes de drenar.</summary>
    public int PendingCount => _queue.Count;

    /// <inheritdoc />
    public void Post(int sessionId, Opcode opcode, ReadOnlySpan<byte> payload)
        => _queue.Enqueue(new WorldMessage(sessionId, opcode, payload.ToArray()));

    /// <summary>Extrae el siguiente mensaje. Sólo debe llamarlo el hilo de simulación.</summary>
    public bool TryDequeue(out WorldMessage message) => _queue.TryDequeue(out message);
}
