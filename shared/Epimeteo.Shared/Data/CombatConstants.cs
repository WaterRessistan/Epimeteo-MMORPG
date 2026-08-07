namespace Epimeteo.Shared.Data;

/// <summary>
/// Números de combate que tienen que valer lo mismo en cliente y servidor, por el mismo motivo
/// que <see cref="InventoryConstants"/>: el cliente necesita el alcance y el cooldown para no
/// pintar un botón que el servidor va a rechazar, y el servidor para validar. Quien manda es
/// siempre el servidor; esto sólo evita que el cliente pida cosas imposibles.
/// </summary>
public static class CombatConstants
{
    /// <summary>Alcance del ataque cuerpo a cuerpo, en tiles, medido de centro a centro.</summary>
    public const float MeleeRangeTiles = 1.5f;

    /// <summary>Cooldown entre ataques, en ms.</summary>
    public const int AttackCooldownMs = 800;

    /// <summary>
    /// Historial de posiciones que guarda el servidor para rebobinar (<c>docs/00 §6</c>). A 20 Hz
    /// son 10 muestras.
    /// </summary>
    public const int PositionHistoryMs = 500;

    /// <summary>
    /// Tope de rebobinado (<c>docs/00 §6</c>). Más allá se valida contra la posición actual: es lo
    /// que acota lo que puede ganar un cliente que mienta con su RTT (FASE-09 §2 D1).
    /// </summary>
    public const int MaxRewindMs = 200;

    /// <summary>Duración del flag de combate PvP (<c>docs/00 §6.2</c>), en ms.</summary>
    public const int CombatFlagMs = 10_000;

    /// <summary>Porcentaje de la XP actual que se pierde al morir en PvP (<c>docs/00 §6.3</c>).</summary>
    public const double PvpXpLossFraction = 0.05;

    /// <summary>Vida con la que se reaparece, como fracción de la máxima.</summary>
    public const double RespawnHpFraction = 0.5;

    /// <summary>Segundos que el saco de loot es exclusivo de quien más daño hizo (FASE-09 §2 D9).</summary>
    public const int LootRightsSeconds = 30;

    /// <summary>Segundos que el saco sigue en el suelo antes de desaparecer.</summary>
    public const int LootDespawnSeconds = 120;
}
