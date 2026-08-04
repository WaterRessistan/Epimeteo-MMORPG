using System;
using System.Collections.Generic;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;
using Godot;

namespace Epimeteo.Client.World;

/// <summary>
/// Dibuja el mundo con <c>_Draw</c> y rectángulos de colores. Es feo a propósito: los assets siguen
/// en placeholder (CLAUDE.md §5) y esta fase valida netcode, no arte. Cuando lleguen los sprites se
/// sustituye este fichero entero sin tocar la predicción ni la interpolación, que es justo por qué
/// el render está separado de ellas.
/// </summary>
public partial class WorldRenderer : Node2D
{
    /// <summary>Lado de un tile en píxeles (CLAUDE.md §2).</summary>
    public const int TilePixels = 16;

    /// <summary>Ancho del personaje en píxeles.</summary>
    private const int BodyWidth = 16;

    /// <summary>Alto del personaje en píxeles: dos tiles, con el pivote en los pies.</summary>
    private const int BodyHeight = 32;

    /// <summary>Paletas de personaje. El índice llega en <c>EntitySpawn</c> y es lo único que hay de aspecto.</summary>
    private static readonly Color[] Palettes =
    [
        new(0.85f, 0.30f, 0.28f),
        new(0.28f, 0.55f, 0.85f),
        new(0.35f, 0.72f, 0.38f),
        new(0.82f, 0.70f, 0.28f),
    ];

    private static readonly Color SolidTile = new(0.16f, 0.17f, 0.20f);
    private static readonly Color FloorTile = new(0.36f, 0.44f, 0.32f);
    private static readonly Color PvpTint = new(0.75f, 0.25f, 0.25f, 0.16f);
    private static readonly Color LocalOutline = new(1f, 1f, 1f, 0.85f);
    private static readonly Color FacingMark = new(0.05f, 0.05f, 0.06f, 0.85f);
    private static readonly Color NameColor = new(0.95f, 0.95f, 0.95f);
    private static readonly Color NameShadow = new(0f, 0f, 0f, 0.7f);

    private readonly List<(Vec2 Pos, Facing Facing, byte Palette, string Name, bool IsLocal)> _drawList = [];

    private GameMap? _map;
    private Font? _font;

    /// <summary>Entidades remotas que hay que pintar. La escena es la dueña; aquí sólo se leen.</summary>
    public IReadOnlyCollection<RemoteEntity> Remotes { get; set; } = [];

    /// <summary>El jugador local, o <c>null</c> mientras no haya entrado.</summary>
    public LocalPlayer? Local { get; set; }

    /// <summary>Nombre del personaje local, para pintarlo encima como el de los demás.</summary>
    public string LocalName { get; set; } = string.Empty;

    /// <summary>Paleta del personaje local.</summary>
    public byte LocalPalette { get; set; }

    /// <summary>Fija el mapa a dibujar. Redibuja de inmediato.</summary>
    public void SetMap(GameMap map)
    {
        _map = map;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Ready() => _font = ThemeDB.FallbackFont;

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_map is null)
        {
            return;
        }

        DrawTiles(_map);
        DrawRegions(_map);
        DrawEntities();
    }

    /// <summary>
    /// Pinta sólo los tiles que caen en pantalla. El mapa es de 96×96 y la ventana enseña unos
    /// 30×17 tiles: sin recortar serían 9216 rectángulos por frame para ver 510, y a 60 fps eso se
    /// nota. El recorte se hace con la transformada real de la cámara, así que sigue valiendo si
    /// mañana se añade zoom.
    /// </summary>
    private void DrawTiles(GameMap map)
    {
        var view = GetViewportTransform().AffineInverse() * GetViewportRect();

        var firstX = Math.Max(0, (int)Math.Floor(view.Position.X / TilePixels));
        var firstY = Math.Max(0, (int)Math.Floor(view.Position.Y / TilePixels));
        var lastX = Math.Min(map.Width - 1, (int)Math.Ceiling(view.End.X / TilePixels));
        var lastY = Math.Min(map.Height - 1, (int)Math.Ceiling(view.End.Y / TilePixels));

        for (var y = firstY; y <= lastY; y++)
        {
            for (var x = firstX; x <= lastX; x++)
            {
                var rect = new Rect2(x * TilePixels, y * TilePixels, TilePixels, TilePixels);
                DrawRect(rect, map.Collision.IsSolid(x, y) ? SolidTile : FloorTile);
            }
        }
    }

    /// <summary>Tiñe las regiones con PvP. El servidor manda los flags; esto sólo los pinta.</summary>
    private void DrawRegions(GameMap map)
    {
        foreach (var region in map.Regions.Regions)
        {
            if (!region.Flags.HasFlag(ZoneFlags.Pvp))
            {
                continue;
            }

            DrawRect(
                new Rect2(
                    region.X * TilePixels,
                    region.Y * TilePixels,
                    region.Width * TilePixels,
                    region.Height * TilePixels),
                PvpTint);
        }
    }

    private void DrawEntities()
    {
        _drawList.Clear();

        foreach (var remote in Remotes)
        {
            _drawList.Add((remote.State.Pos, remote.State.Facing, remote.PaletteIndex, remote.Name, false));
        }

        if (Local is not null)
        {
            _drawList.Add((Local.RenderPos, Local.Current.Facing, LocalPalette, LocalName, true));
        }

        // Y-sort: quien tiene los pies más abajo se dibuja encima, que es lo que da sensación de
        // profundidad en un top-down (CLAUDE.md §2).
        _drawList.Sort(static (a, b) => a.Pos.Y.CompareTo(b.Pos.Y));

        foreach (var entity in _drawList)
        {
            DrawEntity(entity.Pos, entity.Facing, entity.Palette, entity.Name, entity.IsLocal);
        }
    }

    private void DrawEntity(Vec2 pos, Facing facing, byte palette, string name, bool isLocal)
    {
        // La posición es la de los pies; el cuerpo crece hacia arriba.
        var footX = pos.X * TilePixels;
        var footY = pos.Y * TilePixels;
        var body = new Rect2(footX - (BodyWidth / 2f), footY - BodyHeight, BodyWidth, BodyHeight);

        DrawRect(body, Palettes[palette % Palettes.Length]);

        if (isLocal)
        {
            DrawRect(body, LocalOutline, filled: false, width: 1f);
        }

        DrawFacingMark(body, facing);

        if (!string.IsNullOrEmpty(name) && _font is not null)
        {
            var at = new Vector2(footX - (BodyWidth / 2f) - 6f, footY - BodyHeight - 3f);
            DrawString(_font, at + new Vector2(1, 1), name, HorizontalAlignment.Left, -1, 8, NameShadow);
            DrawString(_font, at, name, HorizontalAlignment.Left, -1, 8, NameColor);
        }
    }

    /// <summary>Una muesca en el lado hacia el que mira. Sustituye a la animación mientras no haya sprites.</summary>
    private void DrawFacingMark(Rect2 body, Facing facing)
    {
        const float Thickness = 3f;

        var mark = facing switch
        {
            Facing.North => new Rect2(body.Position.X, body.Position.Y, body.Size.X, Thickness),
            Facing.South => new Rect2(body.Position.X, body.End.Y - Thickness, body.Size.X, Thickness),
            Facing.West => new Rect2(body.Position.X, body.Position.Y, Thickness, body.Size.Y),
            Facing.East => new Rect2(body.End.X - Thickness, body.Position.Y, Thickness, body.Size.Y),
            _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "Orientación desconocida."),
        };

        DrawRect(mark, FacingMark);
    }
}
