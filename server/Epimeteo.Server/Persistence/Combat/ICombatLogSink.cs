namespace Epimeteo.Server.Persistence.Combat;

/// <summary>Frontera de guardado de muertes PvP: el tick encola, un servicio aparte escribe (CLAUDE.md §4).</summary>
public interface ICombatLogSink
{
    void Enqueue(in CombatLogSave save);
}
