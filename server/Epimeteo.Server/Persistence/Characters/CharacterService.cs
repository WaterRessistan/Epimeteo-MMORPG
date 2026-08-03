using Epimeteo.Server.Content;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>
/// Orquesta listar/crear/borrar/seleccionar personaje: valida entrada, resuelve la clase contra
/// <see cref="ClassCatalog"/> y traduce los conflictos del repositorio a <see cref="ResultCode"/>.
/// El manejador de mensajes (hilo de red) sólo traduce estos resultados a los mensajes S2C y a
/// la transición de estado de la sesión (FASE-03-personajes.md §6).
/// </summary>
public sealed class CharacterService(CharacterRepository characters, ClassCatalog classes)
{
    private const int MinSlot = 0;
    private const int MaxSlot = 4;

    public async Task<CharacterSummary[]> ListAsync(long accountId, CancellationToken ct = default)
    {
        var rows = await characters.ListByAccountAsync(accountId, ct).ConfigureAwait(false);
        return rows.Select(ToSummary).ToArray();
    }

    public async Task<CharacterCreateOutcome> CreateAsync(
        long accountId, string name, string classKey, int slot, byte paletteIndex, CancellationToken ct = default)
    {
        if (!IsNameValid(name))
        {
            return CharacterCreateOutcome.Fail(ResultCode.NameInvalid);
        }

        if (!classes.TryGet(classKey, out var classDef))
        {
            return CharacterCreateOutcome.Fail(ResultCode.NameInvalid);
        }

        if (slot is < MinSlot or > MaxSlot)
        {
            // La UI real nunca manda un slot fuera de 0-4 (los 5 botones fijos de la pantalla de
            // selección); esta rama sólo la dispara un cliente manipulado. Desde su perspectiva
            // no hay ningún slot disponible ahí, así que se reutiliza NoCharacterSlots en vez de
            // añadir un código nuevo sólo para un caso que un cliente honesto no puede alcanzar.
            return CharacterCreateOutcome.Fail(ResultCode.NoCharacterSlots);
        }

        var (id, error) = await characters.CreateAsync(accountId, slot, name, classDef, paletteIndex, ct).ConfigureAwait(false);

        return error switch
        {
            CharacterCreateError.SlotOccupied => CharacterCreateOutcome.Fail(ResultCode.SlotOccupied),
            CharacterCreateError.NameTaken => CharacterCreateOutcome.Fail(ResultCode.NameTaken),
            _ => CharacterCreateOutcome.Success(new CharacterSummary
            {
                Id = id!.Value,
                Slot = slot,
                Name = name,
                ClassKey = classDef.Key,
                Level = 1,
                MapKey = "map.village",
                PaletteIndex = paletteIndex,
            }),
        };
    }

    public async Task<CharacterDeleteOutcome> DeleteAsync(
        long accountId, long characterId, bool confirm, CancellationToken ct = default)
    {
        if (!confirm)
        {
            // La confirmación de verdad es un diálogo en Godot, no un segundo secreto: si no
            // llega, se trata como "nada que borrar" en vez de inventar un código nuevo.
            return CharacterDeleteOutcome.Fail(ResultCode.CharacterNotFound);
        }

        var deleted = await characters.SoftDeleteAsync(characterId, accountId, ct).ConfigureAwait(false);
        return deleted ? CharacterDeleteOutcome.Success : CharacterDeleteOutcome.Fail(ResultCode.CharacterNotFound);
    }

    public async Task<CharacterSelectOutcome> SelectAsync(long accountId, long characterId, CancellationToken ct = default)
    {
        var character = await characters.GetOwnedAsync(characterId, accountId, ct).ConfigureAwait(false);
        return character is null
            ? CharacterSelectOutcome.Fail(ResultCode.CharacterNotFound)
            : CharacterSelectOutcome.Success(character);
    }

    private static CharacterSummary ToSummary(Character c) => new()
    {
        Id = c.Id,
        Slot = c.Slot,
        Name = c.Name,
        ClassKey = c.ClassKey,
        Level = c.Level,
        MapKey = c.MapKey,
        PaletteIndex = c.PaletteIndex,
    };

    /// <summary>Espacios incluidos a propósito: un nombre de personaje no es un login (FASE-03-personajes.md §5).</summary>
    private static bool IsNameValid(string name) =>
        name.Length is >= 3 and <= 20 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_');
}
