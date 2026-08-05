using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Vender un stack (o parte) de la bolsa propia. Sólo lo que la tienda abierta compra (FASE-07 §2 D5).</summary>
[MessagePackObject]
public sealed record C2SShopSell
{
    [Key(0)]
    public required ContainerId Container { get; init; }

    [Key(1)]
    public required byte Slot { get; init; }

    [Key(2)]
    public required int Quantity { get; init; }

    /// <summary>Ingreso total esperado (precio unitario × cantidad).</summary>
    [Key(3)]
    public required long ExpectedPrice { get; init; }
}
