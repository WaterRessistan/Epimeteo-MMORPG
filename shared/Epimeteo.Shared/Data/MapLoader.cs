using System.Text.Json;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Convierte el JSON de un mapa en un <see cref="GameMap"/> validado. Cualquier problema es una
/// excepción con el fichero y el motivo: un mapa a medias es peor que no arrancar (CLAUDE.md §4,
/// mismo criterio que <c>ClassCatalog</c>).
/// <para>
/// El cliente Godot no puede usar <see cref="File"/> sobre <c>res://</c>, así que la carga de
/// disco y el parseo están separados: Godot lee el texto con su <c>FileAccess</c> y llama a
/// <see cref="Parse"/>.
/// </para>
/// </summary>
public static class MapLoader
{
    private const int MaxDimension = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Carga y valida un mapa desde disco.</summary>
    public static GameMap Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>Valida y compila un mapa ya leído.</summary>
    /// <param name="json">Contenido del fichero.</param>
    /// <param name="source">De dónde salió, sólo para los mensajes de error.</param>
    public static GameMap Parse(string json, string source)
    {
        MapDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<MapDefinition>(json, JsonOptions)
                ?? throw new InvalidDataException($"{source}: JSON vacío.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source}: JSON inválido — {ex.Message}", ex);
        }

        return Compile(definition, source);
    }

    /// <summary>Valida una definición ya deserializada y la compila.</summary>
    public static GameMap Compile(MapDefinition definition, string source)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Key))
        {
            throw new InvalidDataException($"{source}: falta 'key'.");
        }

        if (definition.Width is < 1 or > MaxDimension || definition.Height is < 1 or > MaxDimension)
        {
            throw new InvalidDataException(
                $"{source}: dimensiones {definition.Width}×{definition.Height} fuera de [1, {MaxDimension}].");
        }

        var solid = ParseCollision(definition, source);
        var collision = new CollisionMap(definition.Width, definition.Height, solid);
        var regions = ParseRegions(definition, source);
        var spawn = ParseSpawn(definition, collision, source);

        return new GameMap(
            definition.Key,
            definition.DisplayName,
            collision,
            new RegionSet(regions),
            spawn,
            (Facing)definition.Spawn.Facing,
            ComputeHash(definition, solid, regions));
    }

    private static bool[] ParseCollision(MapDefinition definition, string source)
    {
        if (definition.Collision.Length != definition.Height)
        {
            throw new InvalidDataException(
                $"{source}: 'collision' tiene {definition.Collision.Length} filas y 'height' dice {definition.Height}.");
        }

        var solid = new bool[definition.Width * definition.Height];

        for (var y = 0; y < definition.Height; y++)
        {
            var row = definition.Collision[y];
            if (row.Length != definition.Width)
            {
                throw new InvalidDataException(
                    $"{source}: la fila {y} tiene {row.Length} tiles y 'width' dice {definition.Width}.");
            }

            for (var x = 0; x < definition.Width; x++)
            {
                solid[(y * definition.Width) + x] = row[x] switch
                {
                    '#' => true,
                    '.' => false,
                    _ => throw new InvalidDataException(
                        $"{source}: carácter '{row[x]}' desconocido en la fila {y}, columna {x} (sólo '#' y '.')."),
                };
            }
        }

        return solid;
    }

    private static MapRegion[] ParseRegions(MapDefinition definition, string source)
    {
        var regions = new MapRegion[definition.Regions.Length];

        for (var i = 0; i < definition.Regions.Length; i++)
        {
            var region = definition.Regions[i];

            if (string.IsNullOrWhiteSpace(region.Name))
            {
                throw new InvalidDataException($"{source}: la región {i} no tiene 'name'.");
            }

            if (region.Rect.Length != 4)
            {
                throw new InvalidDataException(
                    $"{source}: la región '{region.Name}' necesita 'rect' de 4 enteros [x, y, ancho, alto].");
            }

            var (x, y, w, h) = (region.Rect[0], region.Rect[1], region.Rect[2], region.Rect[3]);

            if (w < 1 || h < 1 || x < 0 || y < 0 || x + w > definition.Width || y + h > definition.Height)
            {
                throw new InvalidDataException(
                    $"{source}: la región '{region.Name}' [{x},{y},{w},{h}] se sale del mapa " +
                    $"{definition.Width}×{definition.Height}.");
            }

            var flags = ZoneFlags.None;
            foreach (var flag in region.Flags)
            {
                flags |= flag switch
                {
                    "safe" => ZoneFlags.Safe,
                    "pvp" => ZoneFlags.Pvp,
                    "no_monsters" => ZoneFlags.NoMonsters,
                    "outdoor" => ZoneFlags.Outdoor,
                    "indoor" => ZoneFlags.Indoor,
                    _ => throw new InvalidDataException(
                        $"{source}: flag '{flag}' desconocido en la región '{region.Name}'."),
                };
            }

            if (flags.HasFlag(ZoneFlags.Safe) && flags.HasFlag(ZoneFlags.Pvp))
            {
                throw new InvalidDataException(
                    $"{source}: la región '{region.Name}' es 'safe' y 'pvp' a la vez.");
            }

            regions[i] = new MapRegion(region.Name, x, y, w, h, flags);
        }

        return regions;
    }

    private static Vec2 ParseSpawn(MapDefinition definition, CollisionMap collision, string source)
    {
        var spawn = new Vec2(definition.Spawn.X, definition.Spawn.Y);

        if (definition.Spawn.Facing is < 0 or > 3)
        {
            throw new InvalidDataException($"{source}: 'spawn.facing' vale {definition.Spawn.Facing}, se espera 0–3.");
        }

        if (collision.IsBlocked(spawn, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight))
        {
            throw new InvalidDataException(
                $"{source}: el spawn ({spawn.X}, {spawn.Y}) está dentro de un tile sólido o fuera del mapa.");
        }

        return spawn;
    }

    /// <summary>
    /// FNV-1a de 32 bits sobre todo lo que afecta a la simulación. Se recorren los datos ya
    /// compilados, no el texto: así el hash no cambia por reordenar claves del JSON o retocar el
    /// formato, y sí cambia si se mueve un solo tile.
    /// </summary>
    private static uint ComputeHash(MapDefinition definition, bool[] solid, MapRegion[] regions)
    {
        const uint offsetBasis = 2166136261;
        var hash = offsetBasis;

        HashInt(ref hash, definition.Width);
        HashInt(ref hash, definition.Height);

        foreach (var tile in solid)
        {
            HashByte(ref hash, tile ? (byte)1 : (byte)0);
        }

        foreach (var region in regions)
        {
            foreach (var c in region.Name)
            {
                HashByte(ref hash, (byte)c);
                HashByte(ref hash, (byte)(c >> 8));
            }

            HashInt(ref hash, region.X);
            HashInt(ref hash, region.Y);
            HashInt(ref hash, region.Width);
            HashInt(ref hash, region.Height);
            HashInt(ref hash, (int)region.Flags);
        }

        HashInt(ref hash, BitConverter.SingleToInt32Bits(definition.Spawn.X));
        HashInt(ref hash, BitConverter.SingleToInt32Bits(definition.Spawn.Y));
        HashInt(ref hash, definition.Spawn.Facing);

        return hash;
    }

    private static void HashInt(ref uint hash, int value)
    {
        HashByte(ref hash, (byte)value);
        HashByte(ref hash, (byte)(value >> 8));
        HashByte(ref hash, (byte)(value >> 16));
        HashByte(ref hash, (byte)(value >> 24));
    }

    private static void HashByte(ref uint hash, byte value)
    {
        const uint prime = 16777619;
        hash = (hash ^ value) * prime;
    }
}
