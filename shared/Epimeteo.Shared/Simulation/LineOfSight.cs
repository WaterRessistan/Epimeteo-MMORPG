namespace Epimeteo.Shared.Simulation;

/// <summary>
/// ¿Hay pared entre dos puntos? Trazado incremental (Amanatides–Woo) sobre la rejilla de
/// <see cref="CollisionMap"/>: sin esto se puede pegar a través de la muralla del pueblo, que es
/// justo el tipo de exploit que la Fase 9 tiene que cerrar.
/// <para>
/// Está en <c>Shared</c> porque es geometría pura y determinista, y porque el cliente la
/// necesitará para no ofrecer un objetivo al que el servidor va a rechazar el golpe. Como
/// siempre, quien decide es el servidor.
/// </para>
/// </summary>
public static class LineOfSight
{
    /// <summary>
    /// Verdadero si la recta de <paramref name="from"/> a <paramref name="to"/> no cruza ningún
    /// tile sólido.
    /// <para>
    /// El tile de origen y el de destino <b>no</b> bloquean: una entidad puede estar pegada a un
    /// muro (o dentro de uno, si el mapa cambió bajo sus pies) sin que eso la vuelva inmune.
    /// </para>
    /// </summary>
    public static bool IsClear(CollisionMap map, Vec2 from, Vec2 to)
    {
        ArgumentNullException.ThrowIfNull(map);

        var x = (int)MathF.Floor(from.X);
        var y = (int)MathF.Floor(from.Y);
        var endX = (int)MathF.Floor(to.X);
        var endY = (int)MathF.Floor(to.Y);

        if (x == endX && y == endY)
        {
            return true;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        var stepX = Math.Sign(dx);
        var stepY = Math.Sign(dy);

        // Distancia (en unidades del parámetro t, donde t=1 es el trayecto entero) hasta el primer
        // borde de tile en cada eje, y cuánto avanza t por cada tile cruzado.
        var tDeltaX = dx == 0 ? float.PositiveInfinity : MathF.Abs(1f / dx);
        var tDeltaY = dy == 0 ? float.PositiveInfinity : MathF.Abs(1f / dy);

        var tMaxX = dx == 0
            ? float.PositiveInfinity
            : (dx > 0 ? (x + 1 - from.X) : (from.X - x)) * tDeltaX;
        var tMaxY = dy == 0
            ? float.PositiveInfinity
            : (dy > 0 ? (y + 1 - from.Y) : (from.Y - y)) * tDeltaY;

        // Cota dura de iteraciones: un trayecto no puede cruzar más tiles que la suma de las
        // distancias en cada eje, más uno. Evita cualquier bucle infinito por redondeo.
        var maxSteps = Math.Abs(endX - x) + Math.Abs(endY - y) + 1;

        for (var step = 0; step < maxSteps; step++)
        {
            if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                y += stepY;
                tMaxY += tDeltaY;
            }

            if (x == endX && y == endY)
            {
                return true;
            }

            if (map.IsSolid(x, y))
            {
                return false;
            }
        }

        return true;
    }
}
