using Epimeteo.Server.Inventory;
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
/// <param name="HpMax">Vida máxima, ya resuelta contra el catálogo de clases (sin equipo — FASE-06 §2 D5).</param>
/// <param name="Mp">Maná actual.</param>
/// <param name="MpMax">Maná máximo, sin equipo.</param>
/// <param name="StatStr">Fuerza base guardada.</param>
/// <param name="StatInt">Inteligencia base guardada.</param>
/// <param name="StatVit">Vitalidad base guardada.</param>
/// <param name="StatDex">Destreza base guardada.</param>
/// <param name="Items">Contenedores 0–3 cargados de <c>item_instances</c> (FASE-06 §2 D1).</param>
/// <param name="Gold">Oro guardado (columna <c>gold</c> de <c>characters</c>, Fase 7).</param>
/// <param name="Level">Nivel guardado.</param>
/// <param name="Xp">Experiencia guardada (Fase 9).</param>
/// <param name="StatPoints">Puntos de stat sin gastar (Fase 10).</param>
/// <param name="AccountId">Cuenta dueña del personaje (Fase 11: hace falta para <c>/ban</c>).</param>
/// <param name="IsAdmin">Si la cuenta puede usar los comandos de admin del chat (FASE-11 §2 D6).</param>
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
    int HpMax,
    int Mp,
    int MpMax,
    int StatStr,
    int StatInt,
    int StatVit,
    int StatDex,
    IReadOnlyList<ItemStack> Items,
    long Gold,
    int Level,
    long Xp,
    int StatPoints,
    long AccountId,
    bool IsAdmin);
