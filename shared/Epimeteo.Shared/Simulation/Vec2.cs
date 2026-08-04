namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Punto o desplazamiento en el mundo, en <b>tiles</b> (no en píxeles: el servidor no sabe qué es
/// un píxel, CLAUDE.md §5). El eje Y crece hacia abajo, igual que en pantalla y que en el
/// <c>TileMapLayer</c> de Godot, para no tener que invertir el signo en ningún sitio.
/// <para>
/// A propósito <b>no</b> tiene <c>Length()</c> ni <c>Normalized()</c>: usarían <c>sqrt</c>, que
/// está prohibido en <c>Simulation</c> (ver <see cref="MovementSystem"/> y FASE-04 §2 D2).
/// </para>
/// </summary>
/// <param name="X">Coordenada horizontal en tiles.</param>
/// <param name="Y">Coordenada vertical en tiles, creciente hacia abajo.</param>
public readonly record struct Vec2(float X, float Y)
{
    /// <summary>Vector nulo.</summary>
    public static Vec2 Zero => new(0f, 0f);

    /// <summary>Suma componente a componente.</summary>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Resta componente a componente.</summary>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Escalado por un factor.</summary>
    public static Vec2 operator *(Vec2 v, float k) => new(v.X * k, v.Y * k);

    /// <summary>
    /// Distancia al cuadrado. Se devuelve al cuadrado justamente para no sacar la raíz:
    /// comparar contra un umbral al cuadrado da la misma respuesta y es determinista.
    /// </summary>
    public static float DistanceSquared(Vec2 a, Vec2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Tile que contiene este punto.</summary>
    public TilePos ToTile() => new((int)MathF.Floor(X), (int)MathF.Floor(Y));
}
