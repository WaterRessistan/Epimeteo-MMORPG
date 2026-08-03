using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// El cliente terminó de cargar tras <see cref="S2CWorldEnter"/> (opcode 0x0014, estado
/// <see cref="SessionState.Loading"/>). Sin datos: sólo dispara la transición a
/// <see cref="SessionState.InWorld"/>.
/// </summary>
[MessagePackObject]
public sealed record C2SWorldReady;
