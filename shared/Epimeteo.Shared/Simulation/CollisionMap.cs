namespace Epimeteo.Shared.Simulation;

/// <summary>
/// Rejilla de colisión de un mapa: un bit por tile, sólido o libre. Es lo único que el
/// <see cref="MovementSystem"/> necesita saber del mundo, y por eso es lo único que se comparte
/// entre cliente y servidor.
/// <para>
/// <b>Todo lo que queda fuera del mapa es sólido.</b> Así el borde no necesita un caso especial en
/// el movimiento y un mapa sin muro perimetral tampoco deja escapar a nadie.
/// </para>
/// </summary>
public sealed class CollisionMap
{
    private readonly bool[] _solid;

    /// <param name="width">Ancho en tiles.</param>
    /// <param name="height">Alto en tiles.</param>
    /// <param name="solid">Rejilla en orden fila-mayor, <c>width * height</c> elementos.</param>
    public CollisionMap(int width, int height, bool[] solid)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(solid);

        if (solid.Length != width * height)
        {
            throw new ArgumentException(
                $"La rejilla tiene {solid.Length} tiles y el mapa es {width}×{height}.", nameof(solid));
        }

        Width = width;
        Height = height;
        _solid = solid;
    }

    /// <summary>Ancho del mapa en tiles.</summary>
    public int Width { get; }

    /// <summary>Alto del mapa en tiles.</summary>
    public int Height { get; }

    /// <summary>Verdadero si el tile es sólido o cae fuera del mapa.</summary>
    public bool IsSolid(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)Width || (uint)tileY >= (uint)Height)
        {
            return true;
        }

        return _solid[(tileY * Width) + tileX];
    }

    /// <summary>
    /// Verdadero si la caja centrada en <paramref name="center"/> toca algún tile sólido.
    /// <para>
    /// Los bordes se tratan como abiertos: una caja cuyo lado derecho cae exactamente en
    /// <c>x = 5.0</c> <b>no</b> toca el tile 5. Si no, un pasillo de un tile de ancho sería
    /// intransitable para una caja de exactamente un tile.
    /// </para>
    /// </summary>
    /// <param name="center">Centro de la caja, en tiles.</param>
    /// <param name="halfWidth">Media anchura en tiles.</param>
    /// <param name="halfHeight">Media altura en tiles.</param>
    public bool IsBlocked(Vec2 center, float halfWidth, float halfHeight)
    {
        var minX = center.X - halfWidth;
        var maxX = center.X + halfWidth;
        var minY = center.Y - halfHeight;
        var maxY = center.Y + halfHeight;

        // Floor y Ceiling son operaciones IEEE de redondeo exacto: deterministas en cualquier
        // plataforma, a diferencia de sqrt o las trigonométricas (FASE-04 §2 D2).
        var firstTileX = (int)MathF.Floor(minX);
        var lastTileX = (int)MathF.Ceiling(maxX) - 1;
        var firstTileY = (int)MathF.Floor(minY);
        var lastTileY = (int)MathF.Ceiling(maxY) - 1;

        for (var ty = firstTileY; ty <= lastTileY; ty++)
        {
            for (var tx = firstTileX; tx <= lastTileX; tx++)
            {
                if (IsSolid(tx, ty))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
