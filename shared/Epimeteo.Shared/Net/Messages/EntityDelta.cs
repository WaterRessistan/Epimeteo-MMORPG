using Epimeteo.Shared.Simulation;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Estado de una entidad en un tick concreto. Va dentro de <see cref="S2CSnapshot"/> y sólo se
/// incluye si algo cambió desde el último snapshot que recibió <b>ese</b> observador: una entidad
/// quieta no gasta ancho de banda.
/// </summary>
[MessagePackObject]
public sealed record EntityDelta
{
    [Key(0)]
    public required int Id { get; init; }

    [Key(1)]
    public required float X { get; init; }

    [Key(2)]
    public required float Y { get; init; }

    /// <summary>Velocidad del último paso en tiles/s; el cliente la usa para suavizar.</summary>
    [Key(3)]
    public required float Vx { get; init; }

    [Key(4)]
    public required float Vy { get; init; }

    [Key(5)]
    public required Facing Facing { get; init; }

    [Key(6)]
    public required AnimState Anim { get; init; }

    /// <summary>Bits de estado reservados (invulnerable, montado...). Hoy siempre 0.</summary>
    [Key(7)]
    public required byte Flags { get; init; }
}
