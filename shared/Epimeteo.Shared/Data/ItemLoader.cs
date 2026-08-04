using System.Text.Json;

namespace Epimeteo.Shared.Data;

/// <summary>
/// Parsea y valida un <c>content/items/*.json</c>. Separado de <see cref="ItemCatalog"/> (que
/// sólo recorre el directorio y llama aquí por fichero, igual que <c>MapCatalog</c> llama a
/// <c>MapLoader</c>) para poder probar la validación con JSON en memoria, sin tocar disco.
/// <para>
/// <c>type</c>/<c>equipCategory</c> son texto en el JSON, no números: se parsean a mano en vez de
/// con <c>JsonStringEnumConverter</c> para poder señalar el fichero y el valor exactos en el
/// mensaje de error (mismo criterio que <c>MapLoader</c> con los flags de región).
/// </para>
/// </summary>
public static class ItemLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Lee y valida un fichero. Lanza <see cref="InvalidOperationException"/> si algo no cuadra.</summary>
    public static ItemDefinition Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>La parte pura y testeable: JSON en memoria, con <paramref name="source"/> sólo para los mensajes de error.</summary>
    public static ItemDefinition Parse(string json, string source)
    {
        var raw = JsonSerializer.Deserialize<RawItemDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{source}: JSON vacío o inválido");

        if (string.IsNullOrWhiteSpace(raw.Key))
        {
            throw new InvalidOperationException($"{source}: falta 'key'.");
        }

        if (raw.MaxStack < 1)
        {
            throw new InvalidOperationException($"{source}: 'maxStack' tiene que ser al menos 1.");
        }

        var type = ParseItemType(raw.Type, source);
        var equipCategory = ParseEquipCategory(raw.EquipCategory, source);

        var isEquippable = type is ItemType.Weapon or ItemType.Armor;
        if (isEquippable && equipCategory is null)
        {
            throw new InvalidOperationException(
                $"{source}: los ítems de tipo '{raw.Type}' necesitan 'equipCategory'.");
        }

        if (!isEquippable && equipCategory is not null)
        {
            throw new InvalidOperationException(
                $"{source}: 'equipCategory' sólo tiene sentido en Weapon/Armor, no en '{raw.Type}'.");
        }

        return new ItemDefinition
        {
            Key = raw.Key,
            DisplayName = raw.DisplayName ?? raw.Key,
            Type = type,
            MaxStack = raw.MaxStack,
            EquipCategory = equipCategory,
            BonusStr = raw.BonusStr,
            BonusInt = raw.BonusInt,
            BonusVit = raw.BonusVit,
            BonusDex = raw.BonusDex,
            BonusHp = raw.BonusHp,
            BonusMp = raw.BonusMp,
            HealAmount = raw.HealAmount,
        };
    }

    private static ItemType ParseItemType(string? raw, string source) => raw switch
    {
        "Weapon" => ItemType.Weapon,
        "Armor" => ItemType.Armor,
        "Consumable" => ItemType.Consumable,
        "Material" => ItemType.Material,
        "Seed" => ItemType.Seed,
        _ => throw new InvalidOperationException(
            $"{source}: 'type' desconocido '{raw}' (Weapon, Armor, Consumable, Material, Seed)."),
    };

    private static EquipCategory? ParseEquipCategory(string? raw, string source) => raw switch
    {
        null => null,
        "MainHand" => EquipCategory.MainHand,
        "OffHand" => EquipCategory.OffHand,
        "Head" => EquipCategory.Head,
        "Chest" => EquipCategory.Chest,
        "Hands" => EquipCategory.Hands,
        "Legs" => EquipCategory.Legs,
        "Feet" => EquipCategory.Feet,
        "Cloak" => EquipCategory.Cloak,
        "Ring" => EquipCategory.Ring,
        "Amulet" => EquipCategory.Amulet,
        "Tool" => EquipCategory.Tool,
        _ => throw new InvalidOperationException($"{source}: 'equipCategory' desconocido '{raw}'."),
    };

    /// <summary>Forma cruda del JSON, antes de validar. Nunca sale de este fichero.</summary>
    private sealed record RawItemDefinition
    {
        public string? Key { get; init; }

        public string? DisplayName { get; init; }

        public string? Type { get; init; }

        public int MaxStack { get; init; } = 1;

        public string? EquipCategory { get; init; }

        public int BonusStr { get; init; }

        public int BonusInt { get; init; }

        public int BonusVit { get; init; }

        public int BonusDex { get; init; }

        public int BonusHp { get; init; }

        public int BonusMp { get; init; }

        public int HealAmount { get; init; }
    }
}
