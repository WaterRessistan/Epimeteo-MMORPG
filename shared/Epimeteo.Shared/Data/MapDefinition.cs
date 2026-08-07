namespace Epimeteo.Shared.Data;

/// <summary>
/// Forma cruda de <c>content/maps/*.json</c>, tal cual se deserializa. La versión utilizable —con
/// la colisión ya expandida y las regiones resueltas— es <see cref="GameMap"/>, que produce
/// <see cref="MapLoader"/> tras validar.
/// <para>
/// Vive en <c>Shared</c> porque lo cargan los dos lados: el servidor para simular y el cliente
/// para <b>predecir</b>. Una sola fuente de verdad, en git (CLAUDE.md §3).
/// </para>
/// </summary>
public sealed record MapDefinition
{
    /// <summary>Clave estable del mapa, ej. <c>map.village</c>. Es lo que guarda <c>characters.map_key</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Nombre visible.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Ancho en tiles.</summary>
    public required int Width { get; init; }

    /// <summary>Alto en tiles.</summary>
    public required int Height { get; init; }

    /// <summary>Punto de aparición por defecto.</summary>
    public required MapSpawnDefinition Spawn { get; init; }

    /// <summary>
    /// Una cadena por fila, un carácter por tile: <c>#</c> sólido, <c>.</c> libre. Se eligió texto
    /// y no un array de enteros para que un cambio de mapa se lea en un <c>git diff</c>.
    /// </summary>
    public required string[] Collision { get; init; }

    /// <summary>Regiones con flags. Puede estar vacío: entonces todo el mapa es <c>None</c>.</summary>
    public MapRegionDefinition[] Regions { get; init; } = [];

    /// <summary>
    /// Puntos de aparición de monstruos (Fase 9). Van en el mapa porque son colocación, igual que
    /// las regiones (<c>docs/03</c>: "puntos de spawn en el mapa"). El cliente los ignora — los
    /// monstruos le llegan por <c>EntitySpawn</c> como cualquier otra entidad— y por eso tampoco
    /// entran en el hash del mapa (FASE-09 §2 D14).
    /// </summary>
    public MapSpawnPointDefinition[] Spawns { get; init; } = [];
}

/// <summary>Un punto de aparición de monstruos dentro de un mapa.</summary>
public sealed record MapSpawnPointDefinition
{
    /// <summary>Clave del monstruo, ej. <c>monster.slime</c>.</summary>
    public required string MonsterKey { get; init; }

    public required float X { get; init; }

    public required float Y { get; init; }

    /// <summary>Cuántos mantener vivos a la vez en este punto.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Radio en tiles dentro del que aparecen y patrullan.</summary>
    public float Radius { get; init; } = 4f;

    /// <summary>Segundos entre que uno muere y vuelve a aparecer.</summary>
    public int RespawnSeconds { get; init; } = 30;
}
