namespace Epimeteo.Server.Persistence.Farm;

/// <summary>Frontera de guardado de granja: el tick encola, un servicio aparte escribe (CLAUDE.md §4).</summary>
public interface IFarmSink
{
    void Enqueue(in FarmTileSave save);
}
