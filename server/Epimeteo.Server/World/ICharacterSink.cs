namespace Epimeteo.Server.World;

/// <summary>
/// Destino de los personajes sucios. El tick sólo encola aquí: escribir en la BD dentro del tick
/// pararía la simulación de toda la zona durante la latencia de Postgres (CLAUDE.md §4).
/// </summary>
public interface ICharacterSink
{
    /// <summary>Encola un guardado. Debe retornar inmediatamente y no lanzar nunca.</summary>
    void Enqueue(in CharacterSave save);
}
