namespace Epimeteo.Server.Security;

/// <summary>Qué hacer con una sesión tras apuntarle una anomalía.</summary>
public enum AnomalyVerdict
{
    /// <summary>Por debajo del umbral de aviso: se cuenta y ya.</summary>
    Counted,

    /// <summary>Cruzó el umbral de aviso: log de nivel <c>Warning</c> y fila en <c>anomaly_log</c>.</summary>
    Warn,

    /// <summary>Cruzó el umbral duro: además se cierra la sesión.</summary>
    Kick,
}

/// <summary>
/// Los dos umbrales de cada <see cref="AnomalyKind"/>, por ventana de
/// <see cref="AnomalyRecorder.WindowMs"/> (FASE-13 §2 D4).
/// <para>
/// <b>Son provisionales y a propósito generosos.</b> Sin datos reales de cuántas anomalías produce
/// un jugador honesto con mala conexión, apretarlos sería inventar un número y echar gente por
/// tener 300 ms de latencia. Empiezan altos; <c>anomaly_log</c> existe justamente para poder
/// ajustarlos con datos en vez de con intuición.
/// </para>
/// </summary>
public static class AnomalyThresholds
{
    /// <param name="kind">Tipo de anomalía.</param>
    /// <returns>Cuántas hacen falta en la ventana para avisar, y cuántas para desconectar.</returns>
    public static (int Warn, int Kick) For(AnomalyKind kind) => kind switch
    {
        // Un cliente honesto falla esto de vez en cuando: pides atacar a algo que acaba de morir,
        // o que se movió mientras tu paquete viajaba. Cientos por minuto no.
        AnomalyKind.OutOfRange => (30, 120),

        // El rate limiter ya descarta el mensaje y ya desconecta por su cuenta a los 3 strikes en
        // 10 s. Esto es la vista agregada: alguien que se queda justo por debajo del corte de
        // strikes, una y otra vez, durante un minuto entero.
        AnomalyKind.RateLimited => (20, 80),

        // Idéntico razonamiento para el presupuesto de inputs, que es el control de velocidad
        // (FASE-04 §2 D5): Zone ya desconecta a los 20 strikes, esto ve el patrón sostenido.
        AnomalyKind.InputBudget => (20, 80),

        // Un cliente honesto no manda mensajes fuera de estado: su propia máquina de estados se lo
        // impide. Que pase una vez es una carrera rara; que pase diez, no.
        AnomalyKind.InvalidState => (5, 20),

        // Payload ilegible o dato imposible con el protocolo cerrado. La sesión ya se cierra en el
        // acto casi siempre; esto cuenta los intentos a lo largo de reconexiones.
        AnomalyKind.ProtocolError => (3, 10),

        // Con dinero de por medio conviene enterarse antes: un bucle probando precios es
        // exactamente lo que se quiere ver en el log aunque nunca llegue a colar.
        AnomalyKind.EconomyRejected => (20, 60),

        _ => (30, 120),
    };
}
