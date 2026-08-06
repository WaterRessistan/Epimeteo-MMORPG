using Epimeteo.Server.Content;

namespace Epimeteo.Server.Farm;

/// <summary>
/// Aritmética pura del día de granja: puro y testeable sin BD ni tick, mismo espíritu que
/// <c>Shared/Simulation/MovementSystem</c> (FASE-08 §2 D1, D8, D12).
/// <para>
/// Un día de granja cierra a las 05:00 UTC. En vez de aritmética de calendario (con sus casos
/// límite de mes/año), el índice de día es simplemente cuántos bloques de 24 h exactas han
/// pasado desde una frontera de referencia fija — en UTC no hay horario de verano que rompa esa
/// cuenta.
/// </para>
/// </summary>
public static class FarmCalendar
{
    private const int DayBoundaryHourUtc = 5;

    /// <summary>Cualquier frontera de las 05:00 UTC sirve de referencia; ésta es arbitraria.</summary>
    private static readonly DateTimeOffset ReferenceBoundary =
        new(2000, 1, 1, DayBoundaryHourUtc, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// El día de granja en curso en <paramref name="instant"/>. Sube exactamente en cada frontera
    /// de las 05:00 UTC — usarlo como clave de "ya procesado" es lo que permite recuperar días
    /// perdidos sin más que iterar los índices que faltan (FASE-08 §2 D1).
    /// </summary>
    public static int DayIndex(DateTimeOffset instant) =>
        (int)Math.Floor((instant - ReferenceBoundary).TotalDays);

    /// <summary>El instante de las 05:00 UTC en el que empieza el día <paramref name="dayIndex"/>.</summary>
    public static DateTimeOffset BoundaryOf(int dayIndex) => ReferenceBoundary.AddDays(dayIndex);

    /// <summary>
    /// Estación determinista por día del año en UTC, en cuatro tramos iguales. Sin calendario que
    /// persistir (FASE-08 §2 D8).
    /// </summary>
    public static FarmSeason SeasonOf(DateTimeOffset instant) => instant.UtcDateTime.DayOfYear switch
    {
        <= 91 => FarmSeason.Spring,
        <= 182 => FarmSeason.Summer,
        <= 273 => FarmSeason.Autumn,
        _ => FarmSeason.Winter,
    };

    /// <summary>
    /// Estimación optimista de cuándo estará listo un cultivo, asumiendo riego todos los días que
    /// falten (el mejor caso, +1,0/día — <c>docs/02</c>: "estimación para la UI"). No es un
    /// compromiso, sólo una cota inferior que se recalcula cada vez que cambia el progreso.
    /// </summary>
    public static DateTimeOffset EstimateEta(DateTimeOffset now, float growthDays, float growthNeeded)
    {
        var remaining = growthNeeded - growthDays;
        if (remaining <= 0)
        {
            return now;
        }

        var daysNeeded = (int)Math.Ceiling(remaining);
        return BoundaryOf(DayIndex(now) + daysNeeded);
    }
}
