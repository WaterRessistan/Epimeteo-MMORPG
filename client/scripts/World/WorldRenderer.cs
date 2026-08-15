using System;
using System.Collections.Generic;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;
using Godot;

namespace Epimeteo.Client.World;

/// <summary>
/// Dibuja el mundo con <c>_Draw</c>. Sigue sin tocar la predicción ni la interpolación —siguen
/// viviendo en <c>Shared</c>— por lo que sustituir el arte de aquí nunca ha necesitado tocarlas.
/// Con los sprites CC0 traídos en esta sesión (Kenney, ver <c>assets/ATTRIBUTIONS.md</c>) los
/// tiles y las entidades ya pintan textura real cuando el atlas resuelve la clave; el rectángulo
/// de color se queda sólo de <em>fallback</em> (FASE-12 §2 D3) para lo que aún no tiene entrada,
/// como <c>monster.wolf</c>.
/// </summary>
public partial class WorldRenderer : Node2D
{
    /// <summary>Lado de un tile en píxeles (CLAUDE.md §2).</summary>
    public const int TilePixels = 16;

    /// <summary>Ancho del personaje en píxeles.</summary>
    private const int BodyWidth = 16;

    /// <summary>
    /// Alto del personaje en píxeles. CLAUDE.md §2 fijaba 2 tiles (32px, pivote en los pies) para
    /// un sprite dedicado que nunca llegó a existir; el arte CC0 real disponible (Kenney Tiny
    /// Dungeon) son criaturas chibi de **un** tile, cuerpo entero incluido. Estirar esa imagen a
    /// 32px la habría deformado, así que el alto pasa a 1 tile — ajuste puramente visual, sin
    /// impacto en colisión ni hitbox (que ya vive en `content/`, no en el PNG, CLAUDE.md §5).
    /// </summary>
    private const int BodyHeight = 16;

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

    /// <summary>Dónde vive el manifiesto del atlas (FASE-12 §2 D1). Vacío mientras no haya arte real.</summary>
    private const string AtlasManifestPath = "res://assets/atlas_registry.json";

    private const string GrassTilePath = "res://assets/sprites/tilesets/kenney_roguelike_rpg_pack/grass_floor.png";
    private const string WallTilePath = "res://assets/sprites/tilesets/kenney_roguelike_rpg_pack/stone_wall.png";

    /// <summary>Amplitud del salto de la caminata, en píxeles. 1 px es mucho en un sprite de 16, y se nota.</summary>
    private const float WalkBobAmplitudePx = 1f;

    /// <summary>
    /// Velocidad angular del salto. Con <c>Abs(Sin(x))</c> el bote se repite cada π radianes, así
    /// que esto son ~4 botes/s — un paso a cada lado, cadencia de sprite chibi, no de zancada real.
    /// </summary>
    private const float WalkBobSpeed = 8f * Mathf.Pi;

    private static readonly Color HitFlashColor = new(1f, 0.25f, 0.25f);

    /// <summary>Colores de <see cref="SpawnFloatingText"/> por tipo de evento — públicos para que <c>WorldScreen</c> los use al leer <c>S2CCombatEvent</c>.</summary>
    public static readonly Color CritColor = new(1f, 0.85f, 0.15f);

    public static readonly Color DamageColor = new(0.95f, 0.95f, 0.95f);

    public static readonly Color HealColor = new(0.45f, 0.95f, 0.45f);

    public static readonly Color MissColor = new(0.7f, 0.7f, 0.75f);
    private const double HitFlashDurationMs = 180;
    private const double AttackSwingDurationMs = 130;
    private const double FloatingTextDurationMs = 900;

    private readonly List<(Vec2 Pos, Facing Facing, byte Palette, string DefKey, string Name, bool IsLocal, AnimState Anim, int Id, bool IsAlive)> _drawList = [];
    private readonly Dictionary<string, Texture2D> _atlasTextureCache = [];
    private readonly Dictionary<int, double> _hitFlashUntilMs = [];
    private readonly Dictionary<int, double> _attackSwingUntilMs = [];
    private readonly List<FloatingText> _floatingTexts = [];

    private GameMap? _map;
    private Font? _font;
    private AtlasRegistry _atlas = new(new Dictionary<string, AtlasRegion>());
    private Texture2D? _grassTexture;
    private Texture2D? _wallTexture;

    /// <summary>Un número o palabra que sube y se desvanece sobre una entidad (golpe, curación, esquiva…).</summary>
    private readonly record struct FloatingText(Vec2 WorldPos, string Text, Color Color, double StartMs);

    /// <summary>Entidades remotas que hay que pintar. La escena es la dueña; aquí sólo se leen.</summary>
    public IReadOnlyCollection<RemoteEntity> Remotes { get; set; } = [];

    /// <summary>El jugador local, o <c>null</c> mientras no haya entrado.</summary>
    public LocalPlayer? Local { get; set; }

    /// <summary>Nombre del personaje local, para pintarlo encima como el de los demás.</summary>
    public string LocalName { get; set; } = string.Empty;

    /// <summary>Paleta del personaje local: cae al rectángulo de color si el atlas no resuelve nada para <see cref="LocalDefKey"/>.</summary>
    public byte LocalPalette { get; set; }

    /// <summary>Clave de clase del personaje local (FASE-12 §2 D2: las entidades se buscan en el atlas por <c>defKey</c>).</summary>
    public string LocalDefKey { get; set; } = string.Empty;

    /// <summary>Id de entidad del jugador local, para que <see cref="NotifyHit"/>/<see cref="NotifyAttackSwing"/> lo encuentren en la lista de dibujo.</summary>
    public int LocalEntityId { get; set; } = -1;

    /// <summary>Falso mientras el jugador local esté muerto esperando reaparición: se dibuja atenuado, como cualquier otro cadáver.</summary>
    public bool LocalIsAlive { get; set; } = true;

    /// <summary>Fija el mapa a dibujar. Redibuja de inmediato.</summary>
    public void SetMap(GameMap map)
    {
        _map = map;
        QueueRedraw();
    }

    /// <summary>Tiñe de rojo un instante al que recibe el golpe (FASE-09, <c>S2CCombatEvent</c>). Puramente cosmético.</summary>
    public void NotifyHit(int entityId) => _hitFlashUntilMs[entityId] = Time.GetTicksMsec() + HitFlashDurationMs;

    /// <summary>Resalta un instante al que da el golpe: sin animación de ataque todavía, esto es lo que dice "ha pegado".</summary>
    public void NotifyAttackSwing(int entityId) => _attackSwingUntilMs[entityId] = Time.GetTicksMsec() + AttackSwingDurationMs;

    /// <summary>Un número/palabra que sube y se desvanece sobre <paramref name="worldPos"/> (daño, curación, esquiva, crítico…).</summary>
    public void SpawnFloatingText(Vec2 worldPos, string text, Color color) =>
        _floatingTexts.Add(new FloatingText(worldPos, text, color, Time.GetTicksMsec()));

    /// <inheritdoc />
    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        _atlas = LoadAtlasRegistry();
        _grassTexture = LoadOptionalTexture(GrassTilePath);
        _wallTexture = LoadOptionalTexture(WallTilePath);
    }

    /// <summary>Igual que <see cref="ResolveTexture"/>: la ausencia de fichero no es un error, es el fallback al color plano.</summary>
    private static Texture2D? LoadOptionalTexture(string path) =>
        ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

    /// <summary>
    /// Lee el manifiesto (FASE-12 §2 D1). Puede no existir todavía en cualquier build —el registro
    /// vacío es el estado normal mientras no haya arte real— así que la ausencia no es un error,
    /// a diferencia de <c>ClientContent.LoadMap</c> con un mapa que sí hace falta que exista.
    /// </summary>
    private static AtlasRegistry LoadAtlasRegistry()
    {
        if (!Godot.FileAccess.FileExists(AtlasManifestPath))
        {
            return new AtlasRegistry(new Dictionary<string, AtlasRegion>());
        }

        using var file = Godot.FileAccess.Open(AtlasManifestPath, Godot.FileAccess.ModeFlags.Read);
        var json = file.GetAsText();

        try
        {
            return new AtlasRegistry(AtlasRegistryLoader.Parse(json, AtlasManifestPath));
        }
        catch (InvalidOperationException ex)
        {
            // Un manifiesto roto no debe tirar el cliente entero por algo puramente estético
            // (D3): se avisa y se sigue con el registro vacío, como si no existiera.
            GD.PushError($"atlas_registry.json inválido, se ignora: {ex.Message}");
            return new AtlasRegistry(new Dictionary<string, AtlasRegion>());
        }
    }

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
        DrawFloatingTexts();
    }

    /// <summary>
    /// Pinta sólo los tiles que caen en pantalla. El mapa es de 96×96 y la ventana enseña unos
    /// 30×17 tiles: sin recortar serían 9216 rectángulos por frame para ver 510, y a 60 fps eso se
    /// nota. El recorte se hace con la transformada real de la cámara, así que sigue valiendo si
    /// mañana se añade zoom.
    /// <para>
    /// <b>Ojo con <c>GetViewportTransform()</c>:</b> con <c>window/stretch/mode="canvas_items"</c>
    /// (CLAUDE.md §2, obligatorio para pixel art) esa transformada lleva metida también la escala
    /// de la ventana (×3 a 1440×810), mientras que <c>GetViewportRect()</c> sigue devolviendo el
    /// tamaño lógico de 480×270 — mezclar las dos encogía la zona visible a un tercio y sólo
    /// pintaba una esquina del mapa, con el resto de la pantalla en negro. <c>GetCanvasTransform()</c>
    /// es la transformada de cámara sin la escala de ventana, que es la que hace falta aquí.
    /// </para>
    /// </summary>
    private void DrawTiles(GameMap map)
    {
        var view = GetCanvasTransform().AffineInverse() * GetViewportRect();

        var firstX = Math.Max(0, (int)Math.Floor(view.Position.X / TilePixels));
        var firstY = Math.Max(0, (int)Math.Floor(view.Position.Y / TilePixels));
        var lastX = Math.Min(map.Width - 1, (int)Math.Ceiling(view.End.X / TilePixels));
        var lastY = Math.Min(map.Height - 1, (int)Math.Ceiling(view.End.Y / TilePixels));

        for (var y = firstY; y <= lastY; y++)
        {
            for (var x = firstX; x <= lastX; x++)
            {
                var rect = new Rect2(x * TilePixels, y * TilePixels, TilePixels, TilePixels);
                var solid = map.Collision.IsSolid(x, y);
                var texture = solid ? _wallTexture : _grassTexture;
                if (texture is not null)
                {
                    DrawTextureRect(texture, rect, false);
                }
                else
                {
                    DrawRect(rect, solid ? SolidTile : FloorTile);
                }
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
            _drawList.Add((remote.State.Pos, remote.State.Facing, remote.PaletteIndex, remote.DefKey, remote.Name, false, remote.State.Anim, remote.Id, remote.IsAlive));
        }

        if (Local is not null)
        {
            _drawList.Add((Local.RenderPos, Local.Current.Facing, LocalPalette, LocalDefKey, LocalName, true, Local.Current.Anim, LocalEntityId, LocalIsAlive));
        }

        // Y-sort: quien tiene los pies más abajo se dibuja encima, que es lo que da sensación de
        // profundidad en un top-down (CLAUDE.md §2).
        _drawList.Sort(static (a, b) => a.Pos.Y.CompareTo(b.Pos.Y));

        foreach (var entity in _drawList)
        {
            DrawEntity(entity.Pos, entity.Facing, entity.Palette, entity.DefKey, entity.Name, entity.IsLocal, entity.Anim, entity.Id, entity.IsAlive);
        }
    }

    private void DrawEntity(Vec2 pos, Facing facing, byte palette, string defKey, string name, bool isLocal, AnimState anim, int id, bool isAlive)
    {
        // La posición es la de los pies; el cuerpo crece hacia arriba.
        var footX = pos.X * TilePixels;
        var footY = pos.Y * TilePixels;
        var body = new Rect2(footX - (BodyWidth / 2f), footY - BodyHeight, BodyWidth, BodyHeight);
        body.Position -= new Vector2(0, WalkBobOffset(pos, anim));

        // Muerto: atenuado, sin bote de caminar ni brillo de golpe — un cadáver no anda ni se
        // resalta, sólo se queda ahí hasta el respawn/despawn.
        var modulate = isAlive ? HitFlashModulate(id) : new Color(1f, 1f, 1f, 0.35f);

        var (texture, region) = ResolveTexture(defKey);
        if (texture is not null && region is not null)
        {
            DrawTextureRectRegion(texture, body, new Rect2(region.X, region.Y, region.Width, region.Height), modulate);
        }
        else
        {
            DrawRect(body, Palettes[palette % Palettes.Length] * modulate);
        }

        if (isLocal)
        {
            DrawRect(body, LocalOutline, filled: false, width: 1f);
        }

        if (isAlive)
        {
            DrawFacingMark(body, facing);
            DrawAttackSwing(body, facing, id);
        }

        if (!string.IsNullOrEmpty(name) && _font is not null)
        {
            var at = new Vector2(footX - (BodyWidth / 2f) - 6f, footY - BodyHeight - 3f);
            DrawString(_font, at + new Vector2(1, 1), name, HorizontalAlignment.Left, -1, 8, NameShadow);
            DrawString(_font, at, name, HorizontalAlignment.Left, -1, 8, NameColor);
        }
    }

    /// <summary>Blanco normal, o tirando a rojo si <paramref name="id"/> acaba de recibir un golpe (<see cref="NotifyHit"/>).</summary>
    private Color HitFlashModulate(int id)
    {
        if (!_hitFlashUntilMs.TryGetValue(id, out var until))
        {
            return Colors.White;
        }

        var remaining = until - Time.GetTicksMsec();
        if (remaining <= 0)
        {
            _hitFlashUntilMs.Remove(id);
            return Colors.White;
        }

        return Colors.White.Lerp(HitFlashColor, (float)(remaining / HitFlashDurationMs));
    }

    /// <summary>
    /// Marca en el lado hacia el que se mira, más ancha y brillante un instante tras atacar
    /// (<see cref="NotifyAttackSwing"/>). Sustituye a una animación de ataque de verdad: no hay
    /// spritesheet de golpe en ningún pack CC0 que encajase con <c>kenney_tiny_dungeon</c>.
    /// </summary>
    private void DrawAttackSwing(Rect2 body, Facing facing, int id)
    {
        if (!_attackSwingUntilMs.TryGetValue(id, out var until))
        {
            return;
        }

        var remaining = until - Time.GetTicksMsec();
        if (remaining <= 0)
        {
            _attackSwingUntilMs.Remove(id);
            return;
        }

        var t = (float)(remaining / AttackSwingDurationMs);
        const float Reach = 5f;
        var offset = Reach * t;
        var color = new Color(1f, 1f, 1f, 0.9f * t);

        var swing = facing switch
        {
            Facing.North => new Rect2(body.Position.X - 1, body.Position.Y - offset, body.Size.X + 2, 2f),
            Facing.South => new Rect2(body.Position.X - 1, body.End.Y + offset - 2f, body.Size.X + 2, 2f),
            Facing.West => new Rect2(body.Position.X - offset, body.Position.Y - 1, 2f, body.Size.Y + 2),
            Facing.East => new Rect2(body.End.X + offset - 2f, body.Position.Y - 1, 2f, body.Size.Y + 2),
            _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "Orientación desconocida."),
        };

        DrawRect(swing, color);
    }

    /// <summary>Los números/palabras de <see cref="SpawnFloatingText"/>: suben y se desvanecen; se descartan solos.</summary>
    private void DrawFloatingTexts()
    {
        if (_floatingTexts.Count == 0 || _font is null)
        {
            return;
        }

        var now = Time.GetTicksMsec();
        _floatingTexts.RemoveAll(f => now - f.StartMs > FloatingTextDurationMs);

        foreach (var floating in _floatingTexts)
        {
            var elapsed = now - floating.StartMs;
            var t = (float)(elapsed / FloatingTextDurationMs);
            var rise = 10f * t;
            var alpha = 1f - Mathf.Pow(t, 3);

            var at = new Vector2(
                (floating.WorldPos.X * TilePixels) - 8f,
                (floating.WorldPos.Y * TilePixels) - BodyHeight - 6f - rise);
            var color = new Color(floating.Color.R, floating.Color.G, floating.Color.B, floating.Color.A * alpha);

            DrawString(_font, at + new Vector2(1, 1), floating.Text, HorizontalAlignment.Center, 40, 9, new Color(0f, 0f, 0f, 0.7f * alpha));
            DrawString(_font, at, floating.Text, HorizontalAlignment.Center, 40, 9, color);
        }
    }

    /// <summary>
    /// Bote vertical mientras <c>AnimState.Walk</c> esté activo. No hay spritesheet de zancada de
    /// verdad (los packs CC0 de personajes andando encontrados, o no tenían el mismo estilo chibi
    /// que <c>kenney_tiny_dungeon</c>, o su fichero real estaba detrás de un host al que este
    /// entorno no tiene salida — ver <c>ATTRIBUTIONS.md</c>), así que el bote es la señal de
    /// movimiento hasta que llegue una animación de verdad; <c>AnimState</c> ya lo calcula el
    /// servidor y viaja por predicción/interpolación (<c>Shared/Simulation/MovementSystem.cs</c>),
    /// así que esto es la única pieza que faltaba para usarlo.
    /// <para>
    /// La fase se desincroniza por posición: sin esto, todo el mundo saltaría al mismo tiempo y se
    /// notaría artificial.
    /// </para>
    /// </summary>
    private static float WalkBobOffset(Vec2 pos, AnimState anim)
    {
        if (anim != AnimState.Walk)
        {
            return 0f;
        }

        var phase = (pos.X + pos.Y) * 0.6f;
        var t = Time.GetTicksMsec() / 1000f;
        return Mathf.Abs(Mathf.Sin((t * WalkBobSpeed) + phase)) * WalkBobAmplitudePx;
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

    /// <summary>
    /// Busca <paramref name="defKey"/> en el atlas y, si además el fichero existe de verdad en
    /// disco, la textura ya cargada (con caché por ruta: varias entidades pueden compartir la
    /// misma imagen). <c>(null, null)</c> en cualquier otro caso — ni una clave sin registrar ni
    /// un registro que apunta a un fichero que no está son un error, sólo el estado normal
    /// mientras no haya arte real (FASE-12 §2 D3).
    /// </summary>
    private (Texture2D? Texture, AtlasRegion? Region) ResolveTexture(string defKey)
    {
        if (string.IsNullOrEmpty(defKey) || !_atlas.TryGet(defKey, out var region))
        {
            return (null, null);
        }

        if (!_atlasTextureCache.TryGetValue(region.AtlasPath, out var texture))
        {
            if (!ResourceLoader.Exists(region.AtlasPath))
            {
                return (null, null);
            }

            texture = GD.Load<Texture2D>(region.AtlasPath);
            _atlasTextureCache[region.AtlasPath] = texture;
        }

        return (texture, region);
    }
}
