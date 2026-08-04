using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Todo lo que el mundo necesita saber de un personaje para meterlo en la zona. Se arma en el hilo
/// de red, con la fila que ya se leyó en <c>CharSelect</c>: el tick <b>nunca</b> consulta Postgres
/// (CLAUDE.md §4).
/// </summary>
/// <param name="EntityId">Id de entidad ya reservado, el mismo que viajó en <c>WorldEnter</c>.</param>
/// <param name="CharacterId">Fila de <c>characters</c>.</param>
/// <param name="Name">Nombre del personaje.</param>
/// <param name="ClassKey">Clave de clase, que hace de <c>DefKey</c> de la entidad.</param>
/// <param name="MapKey">Mapa en el que estaba al desconectar.</param>
/// <param name="Position">Posición guardada.</param>
/// <param name="Facing">Orientación guardada.</param>
/// <param name="PaletteIndex">Apariencia placeholder.</param>
/// <param name="Hp">Vida actual.</param>
/// <param name="HpMax">Vida máxima, ya resuelta contra el catálogo de clases.</param>
public sealed record WorldJoinRequest(
    int EntityId,
    long CharacterId,
    string Name,
    string ClassKey,
    string MapKey,
    Vec2 Position,
    Facing Facing,
    byte PaletteIndex,
    int Hp,
    int HpMax);
