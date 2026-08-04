using Epimeteo.Shared.Simulation;

namespace Epimeteo.Shared.Tests;

/// <summary>
/// Construye mapas de colisión a partir de filas de texto, con el mismo alfabeto que
/// <c>content/maps/*.json</c>: <c>#</c> sólido, <c>.</c> libre. Un test de movimiento se lee así
/// de un vistazo.
/// </summary>
internal static class TestMaps
{
    public static CollisionMap From(params string[] rows)
    {
        var height = rows.Length;
        var width = rows[0].Length;
        var solid = new bool[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                solid[(y * width) + x] = rows[y][x] == '#';
            }
        }

        return new CollisionMap(width, height, solid);
    }

    /// <summary>Sala de 8×8 con muro perimetral y nada dentro.</summary>
    public static CollisionMap OpenRoom() => From(
        "########",
        "#......#",
        "#......#",
        "#......#",
        "#......#",
        "#......#",
        "#......#",
        "########");
}
