namespace Epimeteo.WorldBot;

/// <summary>Cómo se mueve un bot durante la corrida.</summary>
internal enum MovementPattern
{
    /// <summary>Cuadrado de 3 s por lado: cambia de dirección y de celda de AOI a menudo.</summary>
    Circulo,

    /// <summary>Empuja siempre en la misma dirección: acaba contra una pared y se queda.</summary>
    Muro,

    /// <summary>Dos bots caminan el uno hacia el otro y luego se separan: prueba el AOI.</summary>
    Encuentro,

    /// <summary>Sube hacia el norte: cruza la muralla del pueblo hacia el campo de PvP.</summary>
    Muralla,

    /// <summary>Baja hacia el sur: vuelve del campo al pueblo por la misma puerta.</summary>
    MurallaVuelta,

    /// <summary>Quieto: sirve de observador.</summary>
    Quieto,
}
