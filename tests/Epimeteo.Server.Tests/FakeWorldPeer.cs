using Epimeteo.Server.World;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Sesión de mentira para los tests del mundo: apunta lo que se le manda en vez de escribirlo en
/// un socket. Existe gracias a que la simulación habla con <see cref="IWorldPeer"/> y no con
/// <c>Session</c>.
/// </summary>
internal sealed class FakeWorldPeer : IWorldPeer
{
    private readonly List<(Opcode Opcode, object Payload)> _sent = [];

    public FakeWorldPeer(int id) => Id = id;

    public int Id { get; }

    /// <summary>RTT simulado, para probar el rebobinado de la compensación de latencia (Fase 9).</summary>
    public int RttMs { get; set; }

    public bool Kicked { get; private set; }

    public KickReason KickedReason { get; private set; }

    public IReadOnlyList<(Opcode Opcode, object Payload)> Sent => _sent;

    public void Send<T>(Opcode opcode, T payload) => _sent.Add((opcode, payload!));

    public void Kick(KickReason reason, ResultCode detail = ResultCode.Ok)
    {
        Kicked = true;
        KickedReason = reason;
    }

    /// <summary>Todos los mensajes de un opcode, en orden de envío.</summary>
    public IEnumerable<T> Messages<T>(Opcode opcode) => _sent
        .Where(entry => entry.Opcode == opcode)
        .Select(entry => (T)entry.Payload);

    /// <summary>El último mensaje de un opcode, o <c>null</c> si no llegó ninguno.</summary>
    public T? Last<T>(Opcode opcode) where T : class => Messages<T>(opcode).LastOrDefault();

    public void Clear() => _sent.Clear();
}
