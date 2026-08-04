namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Números que <b>tienen que</b> valer lo mismo en cliente y servidor. Si uno de estos valores se
/// desincroniza entre los dos lados, la predicción deja de coincidir con la autoridad y el juego
/// se llena de goma elástica sin que nada falle de forma visible. Por eso viven aquí y no en
/// configuración: se compilan en el mismo assembly que usan ambos.
/// </summary>
public static class SimulationConstants
{
    /// <summary>Ticks de simulación por segundo.</summary>
    public const int TickRate = 20;

    /// <summary>Duración de un tick en milisegundos. Un <c>InputState</c> = un tick (FASE-04 §2 D1).</summary>
    public const int TickDtMs = 1000 / TickRate;

    /// <summary>Duración de un tick en segundos.</summary>
    public const float TickDt = 1f / TickRate;

    /// <summary>Inverso de <see cref="TickDt"/>. Multiplicar por él evita una división.</summary>
    public const float InverseTickDt = TickRate;

    /// <summary>Snapshots por segundo (uno de cada dos ticks).</summary>
    public const int SnapshotRate = 10;

    /// <summary>Velocidad de caminar en tiles por segundo: 0.2 tiles por tick, 64 px/s.</summary>
    public const float WalkSpeedTilesPerSec = 4f;

    /// <summary>
    /// Factor de la diagonal, <c>1/√2</c> precalculado. No se normaliza en tiempo de ejecución
    /// porque <c>sqrt</c> está prohibido en la simulación compartida (FASE-04 §2 D2).
    /// </summary>
    public const float DiagonalFactor = 0.70710678f;

    /// <summary>
    /// Media anchura de la caja de colisión del jugador, en tiles. El personaje mide 16×32 px pero
    /// sólo colisionan los pies, como en Zelda/Stardew: caja de 0.75 × 0.5 tiles con el pivote
    /// en los pies.
    /// </summary>
    public const float PlayerHalfWidth = 0.375f;

    /// <summary>Media altura de la caja de colisión del jugador, en tiles.</summary>
    public const float PlayerHalfHeight = 0.25f;

    /// <summary>Lado de una celda de AOI, en tiles (<c>docs/00 § Área de interés</c>).</summary>
    public const int AoiCellTiles = 16;

    /// <summary>
    /// Error tolerado entre la posición predicha y la autoritativa antes de corregir, en tiles.
    /// Es también el colchón que absorbe cualquier diferencia de último bit entre plataformas.
    /// </summary>
    public const float ReconcileToleranceTiles = 0.05f;

    /// <summary>Cuadrado de <see cref="ReconcileToleranceTiles"/>, para comparar sin raíz.</summary>
    public const float ReconcileToleranceSquared = ReconcileToleranceTiles * ReconcileToleranceTiles;

    /// <summary>Retraso del buffer de interpolación de entidades remotas, en milisegundos.</summary>
    public const int InterpolationDelayMs = 100;

    /// <summary>Inputs que el servidor acepta por segundo y sesión antes de contar strike.</summary>
    public const int MaxInputsPerSecond = 26;

    /// <summary>Inputs pendientes en la cola de un jugador antes de descartar los más antiguos.</summary>
    public const int MaxQueuedInputs = 10;

    /// <summary>Cola por encima de la cual el servidor consume dos inputs en un tick para recuperarse.</summary>
    public const int InputCatchUpThreshold = 3;

    /// <summary>Margen sobre la velocidad máxima antes de considerar speedhack.</summary>
    public const float SpeedBudgetTolerance = 1.15f;
}
