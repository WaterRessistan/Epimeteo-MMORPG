namespace Epimeteo.Server.Persistence.Items;

/// <summary>
/// Fila cruda de <c>item_instances</c>, tal como sale de Dapper. <c>Container</c>/<c>Slot</c> son
/// <c>smallint</c> en la BD; se traducen a <c>ContainerId</c>/<c>byte</c> al construir el
/// <c>ItemStack</c> en memoria (<c>Server/Inventory</c>), no aquí — este tipo es sólo transporte.
/// </summary>
public sealed record ItemRow
{
    public required long Id { get; init; }

    public required string DefKey { get; init; }

    public required short Container { get; init; }

    public required short Slot { get; init; }

    public required int Quantity { get; init; }

    public int? Durability { get; init; }

    public int? DurabilityMax { get; init; }

    public required short Quality { get; init; }

    public long? BoundTo { get; init; }
}
