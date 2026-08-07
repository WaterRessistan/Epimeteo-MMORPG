namespace Epimeteo.Shared.Net.Messages;

/// <summary>Qué pasó en un <see cref="S2CCombatEvent"/>.</summary>
public enum CombatEventKind : byte
{
    /// <summary>Daño aplicado.</summary>
    Damage = 0,

    /// <summary>Curación aplicada.</summary>
    Heal = 1,

    /// <summary>El golpe no llegó a hacer daño (esquiva o bloqueo; el detalle va en los flags).</summary>
    Miss = 2,
}
