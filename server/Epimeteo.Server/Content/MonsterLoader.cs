using System.Text.Json;

namespace Epimeteo.Server.Content;

/// <summary>
/// Parsea y valida un <c>content/monsters/*.json</c>. Separado de <see cref="MonsterCatalog"/>
/// para poder probar la validación con JSON en memoria, sin tocar disco — mismo patrón que
/// <c>ItemLoader</c>/<c>ShopLoader</c>/<c>CropLoader</c> (Fases 6–8).
/// </summary>
public static class MonsterLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Lee y valida un fichero. Lanza <see cref="InvalidOperationException"/> si algo no cuadra.</summary>
    public static MonsterDefinition Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>La parte pura y testeable: JSON en memoria, con <paramref name="source"/> sólo para los mensajes de error.</summary>
    public static MonsterDefinition Parse(string json, string source)
    {
        var raw = JsonSerializer.Deserialize<MonsterDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{source}: JSON vacío o inválido");

        if (string.IsNullOrWhiteSpace(raw.Key))
        {
            throw new InvalidOperationException($"{source}: falta 'key'.");
        }

        if (raw.HpMax < 1)
        {
            throw new InvalidOperationException($"{source}: 'hpMax' tiene que ser al menos 1.");
        }

        if (raw.Attack < 0 || raw.Defense < 0)
        {
            throw new InvalidOperationException($"{source}: 'attack' y 'defense' no pueden ser negativos.");
        }

        if (raw.MoveSpeedTiles <= 0)
        {
            throw new InvalidOperationException($"{source}: 'moveSpeedTiles' tiene que ser positivo.");
        }

        if (raw.AttackRangeTiles <= 0)
        {
            throw new InvalidOperationException($"{source}: 'attackRangeTiles' tiene que ser positivo.");
        }

        if (raw.AttackCooldownMs < 1)
        {
            throw new InvalidOperationException($"{source}: 'attackCooldownMs' tiene que ser al menos 1.");
        }

        // La correa tiene que dar más de lo que da el aggro: si no, el monstruo se rendiría antes
        // de llegar a quien acaba de ver y quedaría oscilando entre perseguir y volver.
        if (raw.LeashRadiusTiles <= raw.AggroRadiusTiles)
        {
            throw new InvalidOperationException(
                $"{source}: 'leashRadiusTiles' ({raw.LeashRadiusTiles}) tiene que ser mayor que " +
                $"'aggroRadiusTiles' ({raw.AggroRadiusTiles}).");
        }

        if (raw.XpReward < 0)
        {
            throw new InvalidOperationException($"{source}: 'xpReward' no puede ser negativo.");
        }

        foreach (var loot in raw.Loot)
        {
            if (string.IsNullOrWhiteSpace(loot.DefKey))
            {
                throw new InvalidOperationException($"{source}: hay una entrada de loot sin 'defKey'.");
            }

            if (loot.Chance is < 0 or > 1)
            {
                throw new InvalidOperationException($"{source}: '{loot.DefKey}' tiene 'chance' {loot.Chance}, se espera 0–1.");
            }

            if (loot.Min < 1 || loot.Max < loot.Min)
            {
                throw new InvalidOperationException($"{source}: '{loot.DefKey}' tiene un rango 'min'/'max' imposible.");
            }
        }

        return raw;
    }
}
