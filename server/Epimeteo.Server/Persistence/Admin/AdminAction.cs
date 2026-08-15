namespace Epimeteo.Server.Persistence.Admin;

/// <summary>Qué hizo un administrador, para <c>admin_action_log</c> (FASE-11 §2 D7).</summary>
public enum AdminAction : byte
{
    Kick = 0,
    Ban = 1,
    Teleport = 2,
    Give = 3,

    /// <summary>
    /// Curar del todo (hueco real, pedido explícito de sesión: no había forma de reponerse aparte
    /// de una poción o subir de nivel — ninguna de las dos vale para probar contenido a mano).
    /// </summary>
    Heal = 4,

    /// <summary>Conceder XP directamente — mismo motivo que <see cref="Heal"/>.</summary>
    GrantXp = 5,
}
