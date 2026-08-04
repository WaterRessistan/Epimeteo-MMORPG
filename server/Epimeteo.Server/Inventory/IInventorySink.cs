using Epimeteo.Server.Persistence.Items;

namespace Epimeteo.Server.Inventory;

/// <summary>
/// Destino de las instantáneas de inventario sucias. El tick sólo encola aquí: escribir en la BD
/// dentro del tick pararía la simulación de toda la zona durante la latencia de Postgres
/// (CLAUDE.md §4) — mismo contrato que <c>IPositionSink</c> en la Fase 4.
/// </summary>
public interface IInventorySink
{
    /// <summary>Encola un guardado. Debe retornar inmediatamente y no lanzar nunca.</summary>
    void Enqueue(in InventorySave save);
}
