using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Persistence.Chat;

/// <summary>
/// Una línea de chat, camino de <c>chat_log</c>. Se guarda el texto <b>sin censurar</b>
/// (FASE-11 §2 D9): el registro es para moderación, no para lo que ven los demás jugadores.
/// </summary>
public readonly record struct ChatLogSave(long? CharacterId, ChatChannel Channel, string Body);
