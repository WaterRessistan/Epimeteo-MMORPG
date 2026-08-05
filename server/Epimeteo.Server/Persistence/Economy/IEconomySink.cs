namespace Epimeteo.Server.Persistence.Economy;

/// <summary>
/// Destino de las filas de economía pendientes de escribir. El tick sólo encola aquí: escribir en
/// la BD dentro del tick pararía la simulación de toda la zona (CLAUDE.md §4) — mismo contrato que
/// <c>IPositionSink</c>/<c>IInventorySink</c>.
/// </summary>
public interface IEconomySink
{
    /// <summary>Encola una escritura. Debe retornar inmediatamente y no lanzar nunca.</summary>
    void Enqueue(in EconomySave save);
}
