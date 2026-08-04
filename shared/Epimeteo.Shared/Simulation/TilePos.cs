namespace Epimeteo.Shared.Simulation;

/// <summary>Coordenada entera de tile dentro de un mapa. Origen arriba a la izquierda.</summary>
/// <param name="X">Columna.</param>
/// <param name="Y">Fila.</param>
public readonly record struct TilePos(int X, int Y)
{
    /// <summary>Centro del tile como punto del mundo.</summary>
    public Vec2 Center() => new(X + 0.5f, Y + 0.5f);
}
