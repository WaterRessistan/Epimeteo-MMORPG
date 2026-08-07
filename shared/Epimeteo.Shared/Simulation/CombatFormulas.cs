namespace Epimeteo.Shared.Simulation;

/// <summary>Lo único que las fórmulas necesitan saber de quien pega o recibe.</summary>
/// <param name="Attack">Poder de ataque ya resuelto (base + equipo).</param>
/// <param name="Defense">Defensa ya resuelta.</param>
/// <param name="Dex">Destreza efectiva; decide la probabilidad de crítico.</param>
public readonly record struct CombatantStats(int Attack, int Defense, int Dex);

/// <summary>Resultado de un golpe.</summary>
/// <param name="Damage">Daño final, siempre &gt;= 1.</param>
/// <param name="Critical">Si la tirada salió crítica.</param>
public readonly record struct HitResult(int Damage, bool Critical);

/// <summary>
/// Fórmulas de daño: puras, deterministas y probadas (<c>docs/03</c>, FASE-09 §2 D5). No tocan
/// mundo, ni red, ni reloj — sólo entra estado y una tirada, y sale un número. Quien decide si el
/// golpe está permitido y quien resta la vida es <c>Server/Combat/CombatSystem</c>.
/// <para>
/// Los valores son <b>provisionales</b>, igual que los stats base de las clases: la Fase 10 los
/// reajusta con la curva de progresión real. Lo que esta fase fija es la <i>forma</i>: el daño
/// nunca baja de 1 (un personaje muy blindado encaja poco, pero encaja) y la dispersión y el
/// crítico salen de la misma tirada determinista, para que un test pueda afirmar el número exacto.
/// </para>
/// </summary>
public static class CombatFormulas
{
    /// <summary>Dispersión: ±15 % sobre el daño base.</summary>
    private const int VariancePercent = 15;

    /// <summary>Multiplicador de daño de un crítico.</summary>
    private const double CriticalMultiplier = 2.0;

    /// <summary>Probabilidad de crítico por punto de destreza.</summary>
    private const double CriticalChancePerDex = 0.005;

    /// <summary>Tope de probabilidad de crítico, por alto que sea el stat.</summary>
    private const double MaxCriticalChance = 0.5;

    /// <summary>Probabilidad de crítico de un atacante, en <c>[0, MaxCriticalChance]</c>.</summary>
    public static double CriticalChance(in CombatantStats attacker) =>
        Math.Clamp(attacker.Dex * CriticalChancePerDex, 0, MaxCriticalChance);

    /// <summary>
    /// Daño de un golpe. <paramref name="rng"/> se consume siempre en el mismo orden —dispersión y
    /// luego crítico— para que la secuencia sea reproducible en los tests.
    /// </summary>
    public static HitResult Hit(in CombatantStats attacker, in CombatantStats defender, DeterministicRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var baseDamage = BaseDamage(attacker, defender);

        var spread = rng.NextInt(-VariancePercent, VariancePercent + 1);
        var withSpread = baseDamage * (100 + spread) / 100.0;

        var critical = rng.NextChance(CriticalChance(attacker));
        var final = critical ? withSpread * CriticalMultiplier : withSpread;

        return new HitResult(Math.Max(1, (int)Math.Round(final, MidpointRounding.AwayFromZero)), critical);
    }

    /// <summary>Daño antes de dispersión y crítico. Expuesto para tooltips del cliente y para los tests.</summary>
    public static int BaseDamage(in CombatantStats attacker, in CombatantStats defender) =>
        Math.Max(1, attacker.Attack - (defender.Defense / 2));

    /// <summary>Verdadero si dos entidades están dentro de alcance, medido de centro a centro.</summary>
    public static bool IsWithinRange(Vec2 attacker, Vec2 target, float rangeTiles) =>
        Vec2.DistanceSquared(attacker, target) <= rangeTiles * rangeTiles;
}
