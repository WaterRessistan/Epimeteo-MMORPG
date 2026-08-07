using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>Un hueco de un saco de loot. Mutable: se vacía al cogerlo.</summary>
public sealed class LootSlot
{
    public required string DefKey { get; init; }

    public required int Quantity { get; set; }

    /// <summary>Verdadero cuando ya no queda nada en este hueco.</summary>
    public bool IsEmpty => Quantity <= 0;
}

/// <summary>
/// Un saco de loot en el suelo (FASE-09 §2 D9). Es una <see cref="WorldEntity"/> más, así que
/// aparece y desaparece por el AOI de siempre sin tocar nada de la Fase 4.
/// <para>
/// No es atacable: <see cref="WorldEntity.IsAttackable"/> se queda en falso, que es el valor por
/// defecto y el fallo seguro.
/// </para>
/// </summary>
public sealed class LootBagEntity : WorldEntity
{
    public LootBagEntity(
        int id, Vec2 position, IReadOnlyList<LootSlot> slots, long ownerCharacterId,
        long rightsUntilMs, long despawnAtMs)
        : base(id, EntityType.LootBag, "loot.bag", "Botín", MoveState.AtRest(position, Facing.South))
    {
        Slots = slots;
        OwnerCharacterId = ownerCharacterId;
        RightsUntilMs = rightsUntilMs;
        DespawnAtMs = despawnAtMs;
    }

    public IReadOnlyList<LootSlot> Slots { get; }

    /// <summary>Personaje que más daño hizo: el único que puede abrirlo mientras duren los derechos.</summary>
    public long OwnerCharacterId { get; }

    /// <summary>Hasta cuándo es exclusivo de <see cref="OwnerCharacterId"/>.</summary>
    public long RightsUntilMs { get; }

    /// <summary>Cuándo desaparece del suelo, se haya cogido o no.</summary>
    public long DespawnAtMs { get; }

    /// <summary>Verdadero si ya no queda nada que coger.</summary>
    public bool IsEmpty => Slots.All(slot => slot.IsEmpty);

    /// <summary>
    /// Si <paramref name="characterId"/> puede coger de este saco ahora. Durante el periodo de
    /// derechos sólo su dueño; después, cualquiera.
    /// </summary>
    public bool CanTake(long characterId, long nowMs) =>
        nowMs >= RightsUntilMs || characterId == OwnerCharacterId;

    /// <summary>Lo que ve el cliente. Los huecos vacíos no viajan.</summary>
    public S2CLootDrop ToDropMessage() => new()
    {
        EntityId = Id,
        X = State.Pos.X,
        Y = State.Pos.Y,
        Items = [.. Slots.Where(slot => !slot.IsEmpty).Select(slot => new LootItemInfo
        {
            DefKey = slot.DefKey,
            Quantity = slot.Quantity,
        })],
    };
}
