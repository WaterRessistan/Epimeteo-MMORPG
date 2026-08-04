using Epimeteo.Shared.Simulation;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Todo lo que el cliente necesita para empezar a dibujar una entidad que acaba de entrar en su
/// área de interés. Lo que cambia tick a tick (posición, animación) no va aquí: va en
/// <see cref="EntityDelta"/>, que es mucho más pequeño y viaja 10 veces por segundo.
/// </summary>
[MessagePackObject]
public sealed record EntitySpawnInfo
{
    [Key(0)]
    public required int Id { get; init; }

    [Key(1)]
    public required EntityType Type { get; init; }

    /// <summary>Clave de contenido (<c>class.warrior</c>, y en la Fase 9 <c>monster.slime</c>).</summary>
    [Key(2)]
    public required string DefKey { get; init; }

    [Key(3)]
    public required float X { get; init; }

    [Key(4)]
    public required float Y { get; init; }

    [Key(5)]
    public required Facing Facing { get; init; }

    /// <summary>Apariencia placeholder mientras no haya sprites (Fase 3).</summary>
    [Key(6)]
    public required byte PaletteIndex { get; init; }

    [Key(7)]
    public required string Name { get; init; }

    [Key(8)]
    public required int Hp { get; init; }

    [Key(9)]
    public required int HpMax { get; init; }
}
