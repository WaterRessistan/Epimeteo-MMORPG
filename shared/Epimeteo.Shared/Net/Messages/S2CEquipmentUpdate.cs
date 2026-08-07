using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Estado completo del equipo (container 3) más los stats derivados recalculados (FASE-06 §2 D5).
/// Se manda una vez al entrar al mundo y de nuevo cada vez que <c>Equip</c>/<c>Unequip</c> tiene
/// éxito — es más simple que un delta de equipo (como mucho 12 huecos) y evita que el cliente
/// tenga que reconstruir los stats derivados por su cuenta.
/// </summary>
[MessagePackObject]
public sealed record S2CEquipmentUpdate
{
    /// <summary><c>Slot</c> de cada entrada es un <see cref="Epimeteo.Shared.Data.EquipSlot"/>, no una posición de bolsa.</summary>
    [Key(0)]
    public required ItemStackInfo[] Equipped { get; init; }

    [Key(1)]
    public required int HpMax { get; init; }

    [Key(2)]
    public required int MpMax { get; init; }

    [Key(3)]
    public required int StrEffective { get; init; }

    [Key(4)]
    public required int IntEffective { get; init; }

    [Key(5)]
    public required int VitEffective { get; init; }

    [Key(6)]
    public required int DexEffective { get; init; }

    /// <summary>
    /// Puntos de stat sin gastar (Fase 10). Viaja aquí, no en un mensaje aparte, porque es el
    /// mismo momento en que cambian los stats efectivos: al entrar al mundo, tras equipar/
    /// desequipar y tras <c>AllocateStatPoint</c>.
    /// </summary>
    [Key(7)]
    public required int StatPoints { get; init; }
}
