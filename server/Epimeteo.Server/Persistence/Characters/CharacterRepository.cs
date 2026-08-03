using System.Text.Json;
using Dapper;
using Epimeteo.Server.Content;
using Npgsql;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>Acceso Dapper a la tabla <c>characters</c>. Sólo ve personajes vivos (<c>deleted_at IS NULL</c>).</summary>
public sealed class CharacterRepository(NpgsqlConnectionFactory connections)
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
    /// Crea el personaje con los stats base de <paramref name="classDef"/>. Devuelve el motivo
    /// de conflicto (no una excepción que suba al manejador de mensajes, CLAUDE.md §4) cuando el
    /// slot ya está ocupado por un personaje vivo o el nombre ya está en uso — se distingue por
    /// el nombre del índice único que salta, no adivinando el mensaje de error.
    /// </summary>
    public async Task<(long? Id, CharacterCreateError Error)> CreateAsync(
        long accountId, int slot, string name, ClassDefinition classDef, byte paletteIndex, CancellationToken ct = default)
    {
        var appearance = JsonSerializer.Serialize(new { palette = paletteIndex });

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    INSERT INTO characters
                        (account_id, slot, name, class_key, appearance, stat_str, stat_int, stat_vit, stat_dex, hp, mp)
                    VALUES
                        (@accountId, @slot, @name, @classKey, @appearance::jsonb, @statStr, @statInt, @statVit, @statDex, @hp, @mp)
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
                    },
                    cancellationToken: ct)).ConfigureAwait(false);

            return (id, CharacterCreateError.None);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return ex.ConstraintName switch
            {
                "characters_account_slot_uq" => (null, CharacterCreateError.SlotOccupied),
                "characters_name_uq" => (null, CharacterCreateError.NameTaken),
                _ => (null, CharacterCreateError.SlotOccupied),
            };
        }
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
