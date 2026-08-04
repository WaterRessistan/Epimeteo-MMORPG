namespace Epimeteo.Server.World;

/// <summary>
/// Orden que cruza del hilo de red al de simulación y que no es un mensaje de cliente: entrar al
/// mundo y salir de él. Van por una cola aparte de la de opcodes y se drenan <b>antes</b>, para que
/// un <c>InputState</c> no pueda llegar a la simulación antes que el <c>join</c> de su jugador.
/// </summary>
public abstract record WorldCommand;

/// <summary>El cliente terminó de cargar (<c>WorldReady</c>) y hay que materializar su entidad.</summary>
/// <param name="Peer">Sesión del jugador.</param>
/// <param name="Request">Datos del personaje, ya leídos de la BD.</param>
public sealed record PlayerJoinCommand(IWorldPeer Peer, WorldJoinRequest Request) : WorldCommand;

/// <summary>La sesión se ha ido; hay que sacar la entidad y guardar su posición.</summary>
/// <param name="SessionId">Sesión que se va.</param>
public sealed record PlayerLeaveCommand(int SessionId) : WorldCommand;
