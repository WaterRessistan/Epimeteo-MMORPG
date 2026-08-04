using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;
using Godot;

namespace Epimeteo.Client.World;

/// <summary>
/// Sigue al jugador local sin salirse del mapa.
/// <para>
/// Dos detalles que no son cosméticos con pixel art de 16×16: la posición se redondea a píxel
/// entero (si no, el tile tiembla y las líneas de la rejilla parpadean al andar), y los límites se
/// aplican <b>antes</b> de redondear para que la cámara no se quede a medio píxel del borde.
/// </para>
/// </summary>
public partial class WorldCamera : Camera2D
{
    private Vector2 _halfViewport;
    private Vector2 _mapPixels;

    /// <summary>Fija el mapa que limita el encuadre.</summary>
    public void SetMap(GameMap map) => _mapPixels = new Vector2(
        map.Width * WorldRenderer.TilePixels,
        map.Height * WorldRenderer.TilePixels);

    /// <inheritdoc />
    public override void _Ready()
    {
        // Sin suavizado: la cámara la mueve la posición ya interpolada del jugador, y encadenar
        // dos suavizados hace que el personaje se despegue del centro al cambiar de dirección.
        PositionSmoothingEnabled = false;
        _halfViewport = GetViewportRect().Size / 2f;
    }

    /// <summary>Centra la cámara en una posición del mundo, en tiles.</summary>
    public void FollowTile(Vec2 target)
    {
        var desired = new Vector2(
            target.X * WorldRenderer.TilePixels,
            target.Y * WorldRenderer.TilePixels);

        Position = new Vector2(
            Mathf.Round(Clamp(desired.X, _halfViewport.X, _mapPixels.X)),
            Mathf.Round(Clamp(desired.Y, _halfViewport.Y, _mapPixels.Y)));
    }

    /// <summary>
    /// Limita un eje al mapa. Si el mapa es más pequeño que la pantalla en ese eje, se centra: sin
    /// esto el <c>Clamp</c> daría un mínimo mayor que el máximo y la cámara saltaría.
    /// </summary>
    private static float Clamp(float value, float half, float mapSize) =>
        mapSize <= half * 2f ? mapSize / 2f : Mathf.Clamp(value, half, mapSize - half);
}
