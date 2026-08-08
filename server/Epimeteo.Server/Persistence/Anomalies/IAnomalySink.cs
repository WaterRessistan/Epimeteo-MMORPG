namespace Epimeteo.Server.Persistence.Anomalies;

/// <summary>Frontera de guardado de anomalías: quien detecta encola, un servicio aparte escribe (CLAUDE.md §4).</summary>
public interface IAnomalySink
{
    void Enqueue(in AnomalySave save);
}
