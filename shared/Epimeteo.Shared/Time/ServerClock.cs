using System.Diagnostics;

namespace Epimeteo.Shared.Time;

/// <summary>
/// Reloj monotónico en milisegundos desde el arranque del proceso.
/// Es el <b>único</b> reloj que se usa para lógica de juego, latencia y cooldowns:
/// <see cref="DateTime.Now"/> puede saltar hacia atrás (NTP, cambio de hora) y romper la simulación.
/// Para persistencia sí se usa <c>timestamptz</c> UTC.
/// El cliente lo usa también para sellar sus <c>Ping</c>: el servidor devuelve el valor tal cual
/// y el RTT sale de restar dos lecturas del mismo reloj, sin sincronización entre relojes.
/// </summary>
public static class ServerClock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    /// <summary>Milisegundos monotónicos transcurridos desde el arranque del proceso.</summary>
    public static long NowMs => (long)((Stopwatch.GetTimestamp() - Origin) * TicksToMs);

    /// <summary>Instante monotónico en microsegundos, para medir tiempos de tick.</summary>
    public static long NowUs => (long)((Stopwatch.GetTimestamp() - Origin) * TicksToMs * 1000.0);
}
