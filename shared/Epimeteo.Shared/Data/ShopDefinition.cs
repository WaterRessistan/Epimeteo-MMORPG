namespace Epimeteo.Shared.Data;

/// <summary>Forma de <c>content/shops/*.json</c> (CLAUDE.md §3): una tienda completa, con su tendero dentro.</summary>
public sealed record ShopDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>El armero repara; la tienda general no (FASE-07 §2 D6).</summary>
    public required bool CanRepair { get; init; }

    /// <summary>Cada cuánto se repone todo el stock no infinito de esta tienda (FASE-07 §2 D8).</summary>
    public required int RestockMinutes { get; init; }

    public required ShopNpcPlacement Npc { get; init; }

    /// <summary>El orden es el <c>shopSlot</c> del protocolo. Sin claves repetidas.</summary>
    public required ShopItemDefinition[] Items { get; init; }
}
