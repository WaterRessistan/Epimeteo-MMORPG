using System.Text.Json;
using Dapper;
using Epimeteo.Server.Content;
using Epimeteo.Shared.Data;
using Npgsql;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>Acceso Dapper a la tabla <c>characters</c>. Sólo ve personajes vivos (<c>deleted_at IS NULL</c>).</summary>
public sealed class CharacterRepository(NpgsqlConnectionFactory connections, ItemCatalog items)
{
    private const string SelectColumns = """
        id, account_id AS "AccountId", slot, name, class_key AS "ClassKey", level, xp,
        stat_str AS "StatStr", stat_int AS "StatInt", stat_vit AS "StatVit", stat_dex AS "StatDex",
        stat_points AS "StatPoints", hp, mp, gold, map_key AS "MapKey",
        pos_x AS "PosX", pos_y AS "PosY", facing, appearance::text AS "AppearanceJson"
        """;

    public async Task<IReadOnlyList<Character>> ListByAccountAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Character>(
            new CommandDefinition(
                $"""
                SELECT {SelectColumns}
                  FROM characters
                 WHERE account_id = @accountId AND deleted_at IS NULL
                 ORDER BY slot
                """,
                new { accountId },
                cancellationToken: ct)).ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary><c>null</c> si no existe, está borrado, o no es de <paramref name="accountId"/>.</summary>
    public async Task<Character?> GetOwnedAsync(long characterId, long accountId, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<Character>(
            new CommandDefinition(
                $"""
                SELECT {SelectColumns}
                  FROM characters
                 WHERE id = @characterId AND account_id = @accountId AND deleted_at IS NULL
                """,
                new { characterId, accountId },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Crea el personaje con los stats base de <paramref name="classDef"/> y le da su kit
    /// inicial (<c>classDef.StartingItems</c>, FASE-06 §2 D6), en una sola transacción: un
    /// personaje nunca existe sin su kit ni al revés. Devuelve el motivo de conflicto (no una
    /// excepción que suba al manejador de mensajes, CLAUDE.md §4) cuando el slot ya está ocupado
    /// por un personaje vivo o el nombre ya está en uso — se distingue por el nombre del índice
    /// único que salta, no adivinando el mensaje de error.
    /// </summary>
    public async Task<(long? Id, CharacterCreateError Error)> CreateAsync(
        long accountId, int slot, string name, ClassDefinition classDef, byte paletteIndex, CancellationToken ct = default)
    {
        var appearance = JsonSerializer.Serialize(new { palette = paletteIndex });

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    INSERT INTO characters
                        (account_id, slot, name, class_key, appearance, stat_str, stat_int, stat_vit, stat_dex, hp, mp, gold)
                    VALUES
                        (@accountId, @slot, @name, @classKey, @appearance::jsonb, @statStr, @statInt, @statVit, @statDex, @hp, @mp, @gold)
                    RETURNING id
                    """,
                    new
                    {
                        accountId,
                        slot,
                        name,
                        classKey = classDef.Key,
                        appearance,
                        statStr = classDef.BaseStr,
                        statInt = classDef.BaseInt,
                        statVit = classDef.BaseVit,
                        statDex = classDef.BaseDex,
                        hp = classDef.BaseHp,
                        mp = classDef.BaseMp,
                        gold = classDef.StartingGold,
                    },
                    transaction,
                    cancellationToken: ct)).ConfigureAwait(false);

            await InsertStartingItemsAsync(connection, transaction, id, classDef.StartingItems, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return (id, CharacterCreateError.None);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);

            return ex.ConstraintName switch
            {
                "characters_account_slot_uq" => (null, CharacterCreateError.SlotOccupied),
                "characters_name_uq" => (null, CharacterCreateError.NameTaken),
                _ => (null, CharacterCreateError.SlotOccupied),
            };
        }
    }

    /// <summary>
    /// Un <c>INSERT</c> por entrada de <c>startingItems</c>, cada uno en el contenedor que le
    /// toque a su <c>ItemType</c> (FASE-06 §2 D3) y en el siguiente slot libre de ese contenedor
    /// — el kit lo cura el contenido, así que basta con no repetir slot dentro de la misma clase.
    /// </summary>
    private async Task InsertStartingItemsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long characterId,
        IReadOnlyList<StartingItem> startingItems, CancellationToken ct)
    {
        var nextSlot = new Dictionary<ContainerId, short>();

        foreach (var starting in startingItems)
        {
            if (!items.TryGet(starting.DefKey, out var itemDef))
            {
                throw new InvalidOperationException(
                    $"El kit inicial de una clase referencia '{starting.DefKey}', que no existe en content/items/.");
            }

            var container = InventoryConstants.AllowedContainer(itemDef.Type);
            var slot = nextSlot.GetValueOrDefault(container, (short)0);
            nextSlot[container] = (short)(slot + 1);

            // Un ítem del kit inicial nace con la durabilidad de fábrica llena, si el ítem se
            // desgasta (FASE-07 §4) — igual que uno recién comprado en una tienda
            // (ShopSystem.TryBuy). NULL para los que no la tienen: "no se desgasta" (docs/02).
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO item_instances (def_key, owner_char_id, container, slot, quantity, durability, durability_max)
                    VALUES (@defKey, @characterId, @container, @slot, @quantity, @durability, @durabilityMax)
                    """,
                    new
                    {
                        defKey = starting.DefKey,
                        characterId,
                        container = (short)container,
                        slot,
                        quantity = starting.Quantity,
                        durability = itemDef.DurabilityMax,
                        durabilityMax = itemDef.DurabilityMax,
                    },
                    transaction,
                    cancellationToken: ct)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Vuelca la posición autoritativa de un personaje (Fase 4). La llama la cola de persistencia,
    /// nunca el tick. Actualiza también <c>last_played_at</c>: es el mismo <c>UPDATE</c> y evita
    /// una segunda escritura sobre la misma fila.
    /// </summary>
    /// <summary>
    /// Vuelca en un solo <c>UPDATE</c> todos los escalares que cambian mientras se juega: posición,
    /// oro (Fase 7) y vida/maná/XP/nivel (Fase 9). Son campos de la misma fila y los guarda la
    /// misma cola asíncrona (<c>CharacterSaver</c>): partirlos en varias sentencias sería tener
    /// varios escritores peleándose por la misma fila.
    /// <para>
    /// Vida, maná y XP no se escribían hasta la Fase 9 aunque sus columnas existen desde la Fase 2
    /// — hueco real que sólo se notó al haber combate (FASE-09 §2 D12).
    /// </para>
    /// </summary>
    public async Task<bool> UpdateCharacterStateAsync(
        long characterId, string mapKey, float posX, float posY, int facing, long gold,
        int hp, int mp, long xp, int level, int statStr, int statInt, int statVit, int statDex,
        int statPoints, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE characters
                   SET map_key = @mapKey,
                       pos_x = @posX,
                       pos_y = @posY,
                       facing = @facing,
                       gold = @gold,
                       hp = @hp,
                       mp = @mp,
                       xp = @xp,
                       level = @level,
                       stat_str = @statStr,
                       stat_int = @statInt,
                       stat_vit = @statVit,
                       stat_dex = @statDex,
                       stat_points = @statPoints,
                       last_played_at = now()
                 WHERE id = @characterId AND deleted_at IS NULL
                """,
                new
                {
                    characterId, mapKey, posX, posY, facing = (short)facing, gold, hp, mp, xp, level,
                    statStr, statInt, statVit, statDex, statPoints,
                },
                cancellationToken: ct)).ConfigureAwait(false);

        return affected > 0;
    }

    /// <summary>Verdadero si borró una fila; falso si no existía, ya estaba borrada, o no era de <paramref name="accountId"/>.</summary>
    public async Task<bool> SoftDeleteAsync(long characterId, long accountId, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE characters
                   SET deleted_at = now()
                 WHERE id = @characterId AND account_id = @accountId AND deleted_at IS NULL
                """,
                new { characterId, accountId },
                cancellationToken: ct)).ConfigureAwait(false);

        return affected > 0;
    }
}

public enum CharacterCreateError
{
    None,
    SlotOccupied,
    NameTaken,
}
