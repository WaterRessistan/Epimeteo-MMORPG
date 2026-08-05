using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Client.World;

/// <summary>
/// Una entidad que no controla este cliente: su identidad (quién es y qué aspecto tiene) más un
/// <see cref="EntityInterpolator"/>, que es quien pone la pose.
/// <para>
/// La división no es caprichosa: la parte que decide <b>dónde</b> se dibuja es netcode y vive en
/// <c>Shared</c>, donde se puede probar sin abrir Godot; aquí sólo queda la traducción entre los
/// mensajes de red y esa pieza.
/// </para>
/// </summary>
public sealed class RemoteEntity
{
    private readonly EntityInterpolator _interpolator;

    public RemoteEntity(EntitySpawnInfo info)
    {
        Id = info.Id;
        Type = info.Type;
        DefKey = info.DefKey;
        Name = info.Name;
        PaletteIndex = info.PaletteIndex;
        Hp = info.Hp;
        HpMax = info.HpMax;

        _interpolator = new EntityInterpolator(
            MoveState.AtRest(new Vec2(info.X, info.Y), info.Facing));
    }

    /// <summary>Id de entidad asignado por el servidor.</summary>
    public int Id { get; }

    /// <summary>Qué es: jugador, monstruo, NPC…</summary>
    public EntityType Type { get; }

    /// <summary>
    /// Clave de contenido. Para un <see cref="EntityType.Npc"/> de tienda (Fase 7) es la clave de
    /// la tienda que abre (<c>shop.armory</c>), la misma que trae <c>ShopKey</c> en el JSON.
    /// </summary>
    public string DefKey { get; }

    /// <summary>Nombre visible sobre la entidad.</summary>
    public string Name { get; }

    /// <summary>Índice de paleta, lo único que hay de "aspecto" hasta que haya arte (CLAUDE.md §5).</summary>
    public byte PaletteIndex { get; }

    /// <summary>Vida actual según el último spawn. La Fase 9 la mantendrá al día.</summary>
    public int Hp { get; }

    /// <summary>Vida máxima.</summary>
    public int HpMax { get; }

    /// <summary>Pose que toca dibujar ahora mismo, ya interpolada.</summary>
    public MoveState State => _interpolator.Current;

    /// <summary>Traduce un delta de snapshot a una muestra del interpolador.</summary>
    public void PushSample(long serverTick, EntityDelta delta) => _interpolator.PushSample(
        serverTick,
        new MoveState(
            new Vec2(delta.X, delta.Y),
            new Vec2(delta.Vx, delta.Vy),
            delta.Facing,
            delta.Anim));

    /// <summary>Coloca la entidad en el instante de render y suelta lo que ya no hace falta.</summary>
    public void Advance(double renderTick)
    {
        _interpolator.Interpolate(renderTick);
        _interpolator.TrimBefore(renderTick);
    }
}
