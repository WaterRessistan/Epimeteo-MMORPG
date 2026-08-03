using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Respuesta a <see cref="C2SCharSelect"/> (opcode 0x8013): la sesión pasa a
/// <see cref="SessionState.Loading"/> y este mensaje trae todo lo que el cliente necesita para
/// terminar de cargar antes de mandar <see cref="C2SWorldReady"/>.
/// </summary>
[MessagePackObject]
public sealed record S2CWorldEnter
{
    [Key(0)]
    public required string MapKey { get; init; }

    [Key(1)]
    public required float SpawnX { get; init; }

    [Key(2)]
    public required float SpawnY { get; init; }

    [Key(3)]
    public required int Facing { get; init; }

    /// <summary>
    /// Provisional: hasta que la Fase 4 introduzca un espacio de IDs de entidad real
    /// (<c>EntitySpawn</c>/AOI), vale <c>CharacterId</c> — es lo único estable que hay hoy.
    /// </summary>
    [Key(4)]
    public required long MyEntityId { get; init; }

    [Key(5)]
    public required CharacterStats Stats { get; init; }

    [Key(6)]
    public required long ServerTimeMs { get; init; }
}
