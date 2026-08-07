namespace Epimeteo.Server.Persistence.Admin;

/// <summary>Frontera de guardado de acciones de admin: el tick encola, un servicio aparte escribe (CLAUDE.md §4).</summary>
public interface IAdminActionSink
{
    void Enqueue(in AdminActionSave save);
}
