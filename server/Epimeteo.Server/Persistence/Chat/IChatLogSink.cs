namespace Epimeteo.Server.Persistence.Chat;

/// <summary>Frontera de guardado de chat: el tick encola, un servicio aparte escribe (CLAUDE.md §4).</summary>
public interface IChatLogSink
{
    void Enqueue(in ChatLogSave save);
}
