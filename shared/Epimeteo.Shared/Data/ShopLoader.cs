using System.Text.Json;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Parsea y valida un <c>content/shops/*.json</c>. Separado de <see cref="ShopCatalog"/> (que
/// sólo recorre el directorio y llama aquí por fichero) para poder probar la validación con JSON
/// en memoria, sin tocar disco — mismo patrón que <c>ItemLoader</c>/<c>ItemCatalog</c> (Fase 6).
/// </summary>
public static class ShopLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Lee y valida un fichero. Lanza <see cref="InvalidOperationException"/> si algo no cuadra.</summary>
    public static ShopDefinition Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>La parte pura y testeable: JSON en memoria, con <paramref name="source"/> sólo para los mensajes de error.</summary>
    public static ShopDefinition Parse(string json, string source)
    {
        var raw = JsonSerializer.Deserialize<RawShopDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{source}: JSON vacío o inválido");

        if (string.IsNullOrWhiteSpace(raw.Key))
        {
            throw new InvalidOperationException($"{source}: falta 'key'.");
        }

        if (raw.RestockMinutes < 1)
        {
            throw new InvalidOperationException($"{source}: 'restockMinutes' tiene que ser al menos 1.");
        }

        if (raw.Npc is null)
        {
            throw new InvalidOperationException($"{source}: falta 'npc'.");
        }

        if (string.IsNullOrWhiteSpace(raw.Npc.MapKey))
        {
            throw new InvalidOperationException($"{source}: 'npc.mapKey' es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(raw.Npc.Name))
        {
            throw new InvalidOperationException($"{source}: 'npc.name' es obligatorio.");
        }

        if (raw.Npc.Facing is < 0 or > 3)
        {
            throw new InvalidOperationException($"{source}: 'npc.facing' vale {raw.Npc.Facing}, se espera 0-3.");
        }

        if (raw.Items is null || raw.Items.Length == 0)
        {
            throw new InvalidOperationException($"{source}: 'items' no puede estar vacío.");
        }

        var items = new ShopItemDefinition[raw.Items.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < raw.Items.Length; i++)
        {
            var rawItem = raw.Items[i];

            if (string.IsNullOrWhiteSpace(rawItem.DefKey))
            {
                throw new InvalidOperationException($"{source}: items[{i}] no tiene 'defKey'.");
            }

            if (!seen.Add(rawItem.DefKey))
            {
                throw new InvalidOperationException($"{source}: '{rawItem.DefKey}' está repetido en 'items'.");
            }

            if (rawItem.PriceBuy < 0 || rawItem.PriceSell < 0)
            {
                throw new InvalidOperationException($"{source}: '{rawItem.DefKey}' tiene un precio negativo.");
            }

            if (rawItem.StockMax is < 1)
            {
                throw new InvalidOperationException(
                    $"{source}: '{rawItem.DefKey}' tiene 'stockMax' {rawItem.StockMax}; omite el campo para stock infinito.");
            }

            items[i] = new ShopItemDefinition
            {
                DefKey = rawItem.DefKey,
                PriceBuy = rawItem.PriceBuy,
                PriceSell = rawItem.PriceSell,
                StockMax = rawItem.StockMax,
            };
        }

        return new ShopDefinition
        {
            Key = raw.Key,
            DisplayName = raw.DisplayName ?? raw.Key,
            CanRepair = raw.CanRepair,
            RestockMinutes = raw.RestockMinutes,
            Npc = new ShopNpcPlacement
            {
                MapKey = raw.Npc.MapKey,
                X = raw.Npc.X,
                Y = raw.Npc.Y,
                Facing = (Facing)raw.Npc.Facing,
                Name = raw.Npc.Name,
                PaletteIndex = raw.Npc.PaletteIndex,
            },
            Items = items,
        };
    }

    /// <summary>Forma cruda del JSON, antes de validar. Nunca sale de este fichero.</summary>
    private sealed record RawShopDefinition
    {
        public string? Key { get; init; }

        public string? DisplayName { get; init; }

        public bool CanRepair { get; init; }

        public int RestockMinutes { get; init; } = 60;

        public RawNpc? Npc { get; init; }

        public RawItem[]? Items { get; init; }
    }

    private sealed record RawNpc
    {
        public string? MapKey { get; init; }

        public float X { get; init; }

        public float Y { get; init; }

        public int Facing { get; init; } = 2;

        public string? Name { get; init; }

        public byte PaletteIndex { get; init; }
    }

    private sealed record RawItem
    {
        public string? DefKey { get; init; }

        public long PriceBuy { get; init; }

        public long PriceSell { get; init; }

        public int? StockMax { get; init; }
    }
}
