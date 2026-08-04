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
}
