using Epimeteo.Shared.Simulation;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Un mapa ya cargado y validado, listo para simular. Inmutable: lo comparten todas las entidades
/// de una zona y, en el cliente, la predicción y el render.
/// </summary>
public sealed class GameMap
{
    internal GameMap(
        string key,
        string displayName,
        CollisionMap collision,
        RegionSet regions,
        Vec2 spawn,
        Facing spawnFacing,
        uint hash,
        IReadOnlyList<MapSpawnPointDefinition> spawns)
    {
        Key = key;
        DisplayName = displayName;
        Collision = collision;
        Regions = regions;
        Spawn = spawn;
        SpawnFacing = spawnFacing;
        Hash = hash;
        Spawns = spawns;
    }

    /// <summary>Clave estable, ej. <c>map.village</c>.</summary>
    public string Key { get; }

    /// <summary>Nombre visible.</summary>
    public string DisplayName { get; }

    /// <summary>Rejilla de colisión.</summary>
    public CollisionMap Collision { get; }

    /// <summary>Regiones y sus flags.</summary>
    public RegionSet Regions { get; }

    /// <summary>Puntos de aparición de monstruos (Fase 9). Sólo los usa el servidor.</summary>
    public IReadOnlyList<MapSpawnPointDefinition> Spawns { get; }

    /// <summary>Punto de aparición por defecto.</summary>
    public Vec2 Spawn { get; }

    /// <summary>Orientación al aparecer.</summary>
    public Facing SpawnFacing { get; }

    /// <summary>
    /// Huella de todo lo que afecta a la simulación (dimensiones, colisión, regiones y spawn).
    /// El servidor la manda en <c>WorldEnter</c> y el cliente compara la suya: si no coinciden, el
    /// contenido del cliente está desactualizado y su predicción sería basura. Mejor un error
    /// claro que un desync silencioso que parece culpa del netcode (FASE-04 §2 D4).
    /// </summary>
    public uint Hash { get; }

    /// <summary>Ancho en tiles.</summary>
    public int Width => Collision.Width;

    /// <summary>Alto en tiles.</summary>
    public int Height => Collision.Height;
}
