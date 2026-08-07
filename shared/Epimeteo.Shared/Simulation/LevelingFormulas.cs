namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Curva de experiencia: pura y determinista (FASE-10 §2 D1), mismo espíritu que
/// <c>CombatFormulas</c> — un número exacto, no un rango, para que se pueda probar sin servidor.
/// <para>
/// Lineal a propósito: con los premios de XP de la Fase 9 (8–20 por monstruo, también
/// provisionales) una curva exponencial habría hecho ilegible cualquier prueba manual. La
/// reajusta quien balancee el juego de verdad; esta fase fija la forma.
/// </para>
/// </summary>
public static class LevelingFormulas
{
    private const long XpPerLevel = 100;

    /// <summary>XP que hace falta para pasar de <paramref name="currentLevel"/> al siguiente.</summary>
    public static long XpRequiredForNextLevel(int currentLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(currentLevel, 1);
        return XpPerLevel * currentLevel;
    }
}
