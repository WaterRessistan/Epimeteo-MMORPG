namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Regeneración pasiva de HP/MP: pura y determinista, mismo espíritu que <see cref="CombatFormulas"/>
/// — nada de tiempo real ni de mundo, sólo un número que entra y otro que sale, para que se pueda
/// probar sin servidor.
/// <para>
/// <b>Hallazgo real de esta sesión, no una fase planeada:</b> el maná no se recuperaba nunca — ni
/// con el tiempo, ni al entrar al mundo, ni de ninguna otra forma; <c>content/items/</c> ni
/// siquiera tiene una poción de maná. Cualquier personaje que usara una habilidad (toda la barra
/// 1-3) se quedaba sin poder volver a lanzar nada en cuanto gastaba el maná inicial, que es lo que
/// Mario reportó como "no consigo pegar" — la barra de habilidades que se acababa de mejorar era
/// justo la que se quedaba bloqueada.
/// </para>
/// </summary>
public static class RegenFormulas
{
    /// <summary>Fracción del máximo que se recupera cada segundo. Sube y no baja con el tiempo: no hay penalización por estar en combate (D: mantenerlo simple evita que "por qué no me sube el maná" se confunda con otro bug).</summary>
    public const double HpRegenPerSecondFraction = 0.03;

    public const double MpRegenPerSecondFraction = 0.05;

    /// <summary>
    /// Vida/maná tras <paramref name="elapsedSeconds"/> de regeneración pasiva. Redondea hacia
    /// arriba con un mínimo de 1 punto por segundo transcurrido para que un máximo pequeño (un
    /// guerrero de nivel 1 con 20 de maná) también se note, no sólo los máximos grandes.
    /// </summary>
    public static int Regen(int current, int max, double fractionPerSecond, int elapsedSeconds)
    {
        if (current >= max || max <= 0 || elapsedSeconds <= 0)
        {
            return current;
        }

        var gain = (int)System.Math.Max(elapsedSeconds, System.Math.Round(max * fractionPerSecond * elapsedSeconds));
        return System.Math.Min(max, current + gain);
    }
}
