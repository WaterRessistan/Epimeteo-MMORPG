using Dapper;
using Npgsql;

namespace Epimeteo.Server.Persistence.Items;

/// <summary>
/// Acceso Dapper a <c>item_instances</c>, sólo lo que toca esta fase: contenedores 0–3 (general,
/// armas, armaduras, equipado) de un personaje. Banco, tienda, correo y saco de loot (4–7) son de
/// otras fases y este repositorio no los toca (FASE-06 §1).
/// </summary>
public sealed class ItemRepository(NpgsqlConnectionFactory connections)
{
    private const string SelectColumns = """
        id, def_key AS "DefKey", container AS "Container", slot AS "Slot", quantity AS "Quantity",
        durability AS "Durability", durability_max AS "DurabilityMax", quality AS "Quality",
        bound_to AS "BoundTo"
        """;

    /// <summary>Contenedores 0–3 de un personaje, en cualquier orden. Se llama en <c>CharSelect</c>, fuera del tick.</summary>
    public async Task<IReadOnlyList<ItemRow>> ListByCharacterAsync(long characterId, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ItemRow>(
            new CommandDefinition(
                $"""
                SELECT {SelectColumns}
                  FROM item_instances
                 WHERE owner_char_id = @characterId AND container BETWEEN 0 AND 3
                """,
                new { characterId },
                cancellationToken: ct)).ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>
    /// Reemplaza los contenedores 0–3 de un personaje por <paramref name="items"/> entero, en una
    /// transacción: <c>DELETE</c> de lo que hubiera + <c>INSERT</c> del estado actual. Es la
    /// mitad de Postgres de <c>InventorySaver</c> (FASE-06 §2 D2) — idempotente por diseño: aplicar
    /// la misma instantánea dos veces dos deja el mismo resultado, así que perder una instantánea
    /// vieja de la cola nunca corrompe nada, sólo tarda un guardado más en reflejarse.
    /// </summary>
    public async Task ReplaceInventoryAsync(long characterId, IReadOnlyList<ItemStackSnapshot> items, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM item_instances WHERE owner_char_id = @characterId AND container BETWEEN 0 AND 3",
                new { characterId },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);

        foreach (var item in items)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO item_instances
                        (def_key, owner_char_id, container, slot, quantity, durability, durability_max, quality, bound_to)
                    VALUES
                        (@defKey, @characterId, @container, @slot, @quantity, @durability, @durabilityMax, @quality, @boundTo)
                    """,
                    new
                    {
                        defKey = item.DefKey,
                        characterId,
                        container = (short)item.Container,
                        slot = (short)item.Slot,
                        quantity = item.Quantity,
                        durability = item.Durability,
                        durabilityMax = item.DurabilityMax,
                        quality = (short)item.Quality,
                        boundTo = item.BoundTo,
                    },
                    transaction,
                    cancellationToken: ct)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
