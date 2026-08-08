using Epimeteo.Server.Security;

namespace Epimeteo.Server.Persistence.Anomalies;

/// <summary>
/// Una anomalía que cruzó su umbral, camino de <c>anomaly_log</c> (FASE-13 §2 D7). Sólo se
/// encolan las que avisan o desconectan: guardar cada rechazo llenaría la tabla del ruido normal
/// de una conexión con latencia.
/// </summary>
/// <param name="SessionId">Sesión que la produjo. Siempre se conoce.</param>
/// <param name="CharacterId">Personaje, o <c>null</c> si aún no había elegido (handshake).</param>
/// <param name="AccountId">Cuenta, o <c>null</c> por el mismo motivo.</param>
/// <param name="Kind">Qué clase de rechazo.</param>
/// <param name="CountInWindow">Cuántas llevaba en la ventana al cruzar el umbral.</param>
/// <param name="Verdict">Qué se hizo con la sesión.</param>
/// <param name="RemoteAddress">IP tal como la ve el servidor (real tras el proxy, Fase 5).</param>
public readonly record struct AnomalySave(
    int SessionId,
    long? CharacterId,
    long? AccountId,
    AnomalyKind Kind,
    int CountInWindow,
    AnomalyVerdict Verdict,
    string RemoteAddress);
