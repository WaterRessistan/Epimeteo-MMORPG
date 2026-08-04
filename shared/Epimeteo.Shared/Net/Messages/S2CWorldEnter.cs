using Epimeteo.Shared.Simulation;
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
    public required Facing Facing { get; init; }

    /// <summary>
    /// Id de la entidad del jugador en el mundo, del espacio de ids de entidad (Fase 4). No tiene
    /// nada que ver con <c>CharacterId</c>: es efímero, vive lo que dure la sesión en el mundo, y
    /// es el que aparece en <c>Snapshot</c> y <c>EntitySpawn</c>.
    /// </summary>
    [Key(4)]
    public required int MyEntityId { get; init; }

    [Key(5)]
    public required CharacterStats Stats { get; init; }

    [Key(6)]
    public required long ServerTimeMs { get; init; }

    /// <summary>
    /// Huella del mapa según el servidor (<see cref="Data.GameMap.Hash"/>). El cliente compara la
    /// del fichero que él tiene: si no coinciden, su <c>content/</c> está desactualizado y su
    /// predicción no coincidiría con la simulación. Mejor un error claro que un desync silencioso
    /// (FASE-04 §2 D4).
    /// </summary>
    [Key(7)]
    public required uint MapHash { get; init; }
}
