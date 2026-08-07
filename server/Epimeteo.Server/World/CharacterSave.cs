using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.World;

/// <summary>
/// Instantánea de los escalares de un personaje: todo lo que vive en su fila de <c>characters</c>
/// y cambia mientras juega. Van juntos porque son un solo <c>UPDATE</c> de una sola fila; separar
/// cada campo en su propia cola sería tener varios escritores peleándose por la misma fila.
/// <para>
/// Se llamaba <c>PositionSave</c> hasta la Fase 9. En la Fase 7, al añadirle el oro, decidí no
/// renombrarlo para no tocar código ya probado; ahora que también lleva vida, maná y XP el nombre
/// viejo engaña más de lo que ahorra (FASE-09 §2 D12).
/// </para>
/// <para>
/// <b>Vida, maná y XP no se persistían hasta la Fase 9</b>: las columnas existen desde la Fase 2 y
/// se leían en <c>CharSelect</c>, pero nadie las escribía. Daba igual mientras nada las cambiara;
/// con combate, un moribundo se curaría del todo reconectando.
/// </para>
/// </summary>
/// <param name="CharacterId">Fila de <c>characters</c>.</param>
/// <param name="MapKey">Mapa.</param>
/// <param name="X">Coordenada X en tiles.</param>
/// <param name="Y">Coordenada Y en tiles.</param>
/// <param name="Facing">Orientación.</param>
/// <param name="Gold">Oro actual.</param>
/// <param name="Hp">Vida actual.</param>
/// <param name="Mp">Maná actual.</param>
/// <param name="Xp">Experiencia acumulada.</param>
/// <param name="Level">Nivel.</param>
/// <param name="StatStr">Fuerza base (sin equipo).</param>
/// <param name="StatInt">Inteligencia base.</param>
/// <param name="StatVit">Vitalidad base.</param>
/// <param name="StatDex">Destreza base.</param>
/// <param name="StatPoints">
/// Puntos de stat sin gastar (Fase 10). Hueco real cerrado de paso: estas cinco columnas existen
/// desde la Fase 2, se leían en <c>CharSelect</c> y nadie las escribía — mismo hueco que
/// <c>hp/mp/xp/level</c> en la Fase 9 (D12 de aquella fase), esta vez para los stats base.
/// </param>
public readonly record struct CharacterSave(
    long CharacterId,
    string MapKey,
    float X,
    float Y,
    Facing Facing,
    long Gold,
    int Hp,
    int Mp,
    long Xp,
    int Level,
    int StatStr,
    int StatInt,
    int StatVit,
    int StatDex,
    int StatPoints);
