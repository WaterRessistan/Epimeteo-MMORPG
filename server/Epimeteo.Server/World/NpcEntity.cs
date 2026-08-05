using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Un tendero, estático para siempre desde que se crea (FASE-07 §2 D3). Sin cola de inputs, sin
/// inventario propio, sin peer: nadie lo controla, sólo existe para que <c>AoiSystem</c> lo
/// encuentre y lo mande en <c>EntitySpawn</c> a quien se acerque — reutiliza esa maquinaria tal
/// cual, sin tocarla.
/// </summary>
public sealed class NpcEntity : WorldEntity
{
    public NpcEntity(int id, string shopKey, string name, Vec2 position, Facing facing)
        : base(id, EntityType.Npc, shopKey, name, MoveState.AtRest(position, facing))
    {
        ShopKey = shopKey;
    }

    /// <summary>La tienda que abre este NPC. Mismo valor que <see cref="WorldEntity.DefKey"/>, con nombre propio para que se lea en <c>ShopSystem</c>.</summary>
    public string ShopKey { get; }
}
