using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Posición <b>y oro</b> de un personaje, listos para volcarse a Postgres en el mismo <c>UPDATE</c>
/// (FASE-07 §2 D2): son escalares de la misma fila de <c>characters</c>, así que comparten
/// guardado en vez de tener cada uno su propia cola — el nombre se queda por no arrastrar el
/// cambio a todo lo que ya usa esta clase desde la Fase 4.
/// </summary>
/// <param name="CharacterId">Fila de <c>characters</c>.</param>
/// <param name="MapKey">Mapa.</param>
/// <param name="X">Coordenada X en tiles.</param>
/// <param name="Y">Coordenada Y en tiles.</param>
/// <param name="Facing">Orientación.</param>
/// <param name="Gold">Oro actual.</param>
public readonly record struct PositionSave(long CharacterId, string MapKey, float X, float Y, Facing Facing, long Gold);
