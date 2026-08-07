namespace Epimeteo.Shared.Net;

/// <summary>
/// Canal de un mensaje de chat (FASE-11 §2 D1/D2). El cliente sólo elige <see cref="Global"/> o
/// <see cref="Zone"/> al mandar; <see cref="Whisper"/> lo pone el servidor al reenviar un
/// <c>/w</c>, y <see cref="System"/> queda reservado para avisos del servidor — nadie lo emite
/// todavía (FASE-11 §1, fuera de alcance a propósito).
/// </summary>
public enum ChatChannel : byte
{
    Global = 0,
    Zone = 1,
    Whisper = 2,
    System = 3,
}
