using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.World;

/// <summary>Mensaje de cliente ya copiado y listo para procesarse dentro del tick.</summary>
/// <param name="SessionId">Sesión de origen.</param>
/// <param name="Opcode">Opcode ya validado contra <see cref="OpcodeTable"/>.</param>
/// <param name="Payload">Copia propia del payload MessagePack.</param>
public readonly record struct WorldMessage(int SessionId, Opcode Opcode, byte[] Payload);

/// <summary>
/// Implementación por defecto de <see cref="IWorldInbox"/>: dos colas concurrentes sin bloqueos
/// entre el hilo de red (productores) y el de simulación (consumidor único).
/// </summary>
public sealed class WorldInbox : IWorldInbox
{
    private readonly ConcurrentQueue<WorldMessage> _messages = new();
    private readonly ConcurrentQueue<WorldCommand> _control = new();

    /// <summary>Mensajes pendientes de drenar.</summary>
    public int PendingCount => _messages.Count;

    /// <inheritdoc />
    public void Post(int sessionId, Opcode opcode, ReadOnlySpan<byte> payload)
        => _messages.Enqueue(new WorldMessage(sessionId, opcode, payload.ToArray()));

    /// <inheritdoc />
    public void PostControl(WorldCommand command) => _control.Enqueue(command);

    /// <summary>Extrae el siguiente mensaje. Sólo debe llamarlo el hilo de simulación.</summary>
    public bool TryDequeue(out WorldMessage message) => _messages.TryDequeue(out message);

    /// <summary>Extrae la siguiente orden de control. Sólo debe llamarla el hilo de simulación.</summary>
    public bool TryDequeueControl([MaybeNullWhen(false)] out WorldCommand command) => _control.TryDequeue(out command);
}
