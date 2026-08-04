using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>Posición de un personaje lista para volcarse a Postgres.</summary>
/// <param name="CharacterId">Fila de <c>characters</c>.</param>
/// <param name="MapKey">Mapa.</param>
/// <param name="X">Coordenada X en tiles.</param>
/// <param name="Y">Coordenada Y en tiles.</param>
/// <param name="Facing">Orientación.</param>
public readonly record struct PositionSave(long CharacterId, string MapKey, float X, float Y, Facing Facing);
