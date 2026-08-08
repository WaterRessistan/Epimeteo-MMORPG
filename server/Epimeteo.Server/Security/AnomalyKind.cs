namespace Epimeteo.Server.Security;

/// <summary>
/// Qué clase de rechazo se le apunta a una sesión (FASE-13 §2 D4). Cada uno tiene su propio
/// umbral: 30 acciones fuera de alcance en un minuto es sospechoso pero explicable con mala
/// latencia; 30 errores de protocolo no se explican con nada honesto.
/// <para>
/// Los valores son estables: se persisten en <c>anomaly_log.kind</c>.
/// </para>
/// </summary>
public enum AnomalyKind : short
{
    /// <summary>Acción con objetivo fuera de alcance, o contra un objetivo que no existe.</summary>
    OutOfRange = 0,

    /// <summary>Superó el cubo de fichas de su familia de opcode (<c>SessionRateLimiter</c>).</summary>
    RateLimited = 1,

    /// <summary>Se pasó del presupuesto de inputs por segundo (<c>InputQueue</c>, el control de velocidad).</summary>
    InputBudget = 2,

    /// <summary>Mandó un opcode legal en un estado de sesión que no lo permite.</summary>
    InvalidState = 3,

    /// <summary>Payload ilegible, opcode desconocido o dato imposible con el protocolo cerrado.</summary>
    ProtocolError = 4,

    /// <summary>Rechazo con dinero de por medio: precio que no cuadra, oro insuficiente, stock.</summary>
    EconomyRejected = 5,
}
