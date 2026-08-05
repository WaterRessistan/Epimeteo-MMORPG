using System;
using System.Collections.Generic;
using Epimeteo.Client.Net;
using Epimeteo.Client.Shop;
using Epimeteo.Client.Ui;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Godot;

namespace Epimeteo.Client.World;

/// <summary>
/// La escena de mundo. Carga el mapa, comprueba que es el mismo que el del servidor, mantiene el
/// registro de entidades y reparte cada mensaje a quien le toca: el jugador local predice, las
/// entidades remotas interpolan.
/// </summary>
public partial class WorldScreen : Node2D
{
    /// <summary>
    /// Rango cliente para decidir a qué NPC apunta <c>interact</c>. Con margen sobre el del
    /// servidor (<c>ShopInteractionRangeTiles</c> = 3, FASE-07 §2 D7) a propósito: el servidor
    /// tiene la última palabra, así que ser un poco generoso aquí sólo hace que
    /// <c>ShopOpen</c> se rechace con <c>TooFarAway</c> alguna vez de más, nunca que se acepte algo
    /// que no debía.
    /// </summary>
    private const float InteractRangeTiles = 3.5f;

    private readonly Dictionary<int, RemoteEntity> _remotes = [];

    /// <summary>Reloj de interpolación. Vive en <c>Shared</c> y tiene sus propios tests.</summary>
    private readonly InterpolationClock _clock = new();

    private NetClient _net = null!;
    private WorldRenderer _renderer = null!;
    private WorldCamera _camera = null!;
    private WorldHud _hud = null!;
    private ShopScreen _shop = null!;

    private LocalPlayer? _local;
    private int _myEntityId;

    private string _regionName = string.Empty;
    private ZoneFlags _regionFlags = ZoneFlags.None;

    /// <inheritdoc />
    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");
        _renderer = GetNode<WorldRenderer>("Renderer");
        _camera = GetNode<WorldCamera>("Camera");
        _hud = GetNode<WorldHud>("Hud/WorldHud");
        _shop = GetNode<ShopScreen>("ShopScreen");

        var enter = _net.LastWorldEnter;
        if (enter is null)
        {
            Fail("Se entró al mundo sin WorldEnter.");
            return;
        }

        var map = ClientContent.LoadMap(enter.MapKey);
        if (map is null)
        {
            Fail($"Falta el mapa {enter.MapKey} en content/.");
            return;
        }

        // El contenido del cliente tiene que ser bit a bit el del servidor: si no, la predicción
        // se hace sobre otra colisión y el jugador vería goma elástica sin causa aparente. Mejor
        // pararse aquí con un mensaje claro (FASE-04 §2 D4).
        if (map.Hash != enter.MapHash)
        {
            Fail($"Contenido desactualizado: el mapa del servidor es {enter.MapHash:X8} y el tuyo {map.Hash:X8}.");
            return;
        }

        _myEntityId = enter.MyEntityId;

        _renderer.SetMap(map);
        _camera.SetMap(map);

        _local = new LocalPlayer(_net, map, new Vec2(enter.SpawnX, enter.SpawnY), enter.Facing);
        _renderer.Local = _local;
        _renderer.Remotes = _remotes.Values;
        _renderer.LocalName = _net.SelectedCharacter?.Name ?? string.Empty;
        _renderer.LocalPalette = _net.SelectedCharacter?.PaletteIndex ?? 0;
        _camera.FollowTile(_local.RenderPos);

        _net.SnapshotReceived += OnSnapshot;
        _net.EntitySpawnReceived += OnEntitySpawn;
        _net.EntityDespawnReceived += OnEntityDespawn;
        _net.ZoneFlagsUpdateReceived += OnZoneFlags;
        _net.Kicked += OnKicked;

        // Hasta aquí sólo se ha preparado el cliente. WorldReady le dice al servidor que ya puede
        // meter la entidad en el mundo y empezar a mandar snapshots.
        _net.SendWorldReady();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _net.SnapshotReceived -= OnSnapshot;
        _net.EntitySpawnReceived -= OnEntitySpawn;
        _net.EntityDespawnReceived -= OnEntityDespawn;
        _net.ZoneFlagsUpdateReceived -= OnZoneFlags;
        _net.Kicked -= OnKicked;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_local is null)
        {
            return;
        }

        _local.Update(delta);
        _clock.Advance(delta);

        foreach (var remote in _remotes.Values)
        {
            remote.Advance(_clock.RenderTick);
        }

        _camera.FollowTile(_local.RenderPos);
        _renderer.QueueRedraw();
        UpdateHud();
        HandleInteract();
    }

    /// <summary>
    /// Tecla <c>interact</c> (E): si la tienda está abierta, la cierra; si no, abre la del NPC
    /// más cercano dentro de rango. El servidor decide de verdad si la distancia vale
    /// (FASE-07 §2 D7) — esto sólo elige a qué NPC apuntar.
    /// </summary>
    private void HandleInteract()
    {
        if (!Input.IsActionJustPressed(InputActions.Interact) || _local is null)
        {
            return;
        }

        if (_shop.IsOpen)
        {
            _shop.RequestClose();
            return;
        }

        var npc = FindNearestNpc(_local.Current.Pos);
        if (npc is null)
        {
            return;
        }

        _shop.NoteOpenedNpc(npc.Id);
        _net.SendShopOpen(npc.Id);
    }

    private RemoteEntity? FindNearestNpc(Vec2 fromPos)
    {
        RemoteEntity? nearest = null;
        var nearestDistanceSq = InteractRangeTiles * InteractRangeTiles;

        foreach (var remote in _remotes.Values)
        {
            if (remote.Type != EntityType.Npc)
            {
                continue;
            }

            var distanceSq = Vec2.DistanceSquared(fromPos, remote.State.Pos);
            if (distanceSq <= nearestDistanceSq)
            {
                nearest = remote;
                nearestDistanceSq = distanceSq;
            }
        }

        return nearest;
    }

    private void OnSnapshot(S2CSnapshot snapshot)
    {
        _clock.OnSnapshot(snapshot.ServerTick);

        foreach (var delta in snapshot.Entities)
        {
            if (delta.Id == _myEntityId)
            {
                _local?.ApplyAuthoritative(delta, snapshot.LastAckedInputSeq);
            }
            else if (_remotes.TryGetValue(delta.Id, out var remote))
            {
                remote.PushSample(snapshot.ServerTick, delta);
            }

            // Un delta de una entidad sin spawn previo se ignora: es la carrera normal entre un
            // snapshot en vuelo y el EntityDespawn que ya se procesó.
        }
    }

    private void OnEntitySpawn(S2CEntitySpawn spawn)
    {
        foreach (var info in spawn.Entities)
        {
            if (info.Id == _myEntityId)
            {
                continue;
            }

            _remotes[info.Id] = new RemoteEntity(info);
        }
    }

    private void OnEntityDespawn(S2CEntityDespawn despawn)
    {
        foreach (var entry in despawn.Entities)
        {
            _remotes.Remove(entry.Id);
        }
    }

    /// <summary>
    /// Expulsado estando dentro. Se vuelve a la pantalla de conexión, igual que hacen las demás
    /// pantallas: quedarse en un mundo congelado sin decir nada es lo peor que puede hacer aquí el
    /// cliente. El motivo llega en el mensaje porque el servidor lo manda antes de cerrar.
    /// </summary>
    private void OnKicked(KickReason reason, ResultCode detail, int serverProtocolVersion)
    {
        GD.Print(ResultCodeText.Describe(reason, detail, serverProtocolVersion));
        GetTree().ChangeSceneToFile("res://scenes/Connect.tscn");
    }

    private void OnZoneFlags(S2CZoneFlagsUpdate update)
    {
        _regionName = update.RegionName;
        _regionFlags = update.Flags;
    }

    private void UpdateHud()
    {
        if (_local is null)
        {
            return;
        }

        _hud.SetPosition(_local.Current.Pos, _remotes.Count);
        _hud.SetNetwork(_net.LastRttMs, _net.SimulatedLagMs);
        _hud.SetPrediction(
            _local.Prediction.Corrections,
            _local.Prediction.MaxErrorTiles,
            _local.Prediction.PendingCount);

        // Mientras no llegue el primer ZoneFlagsUpdate se enseña lo que el cliente deduce del
        // mapa; en cuanto el servidor habla, manda él.
        if (string.IsNullOrEmpty(_regionName))
        {
            var region = _local.Region;
            _hud.SetRegion(region.Name, region.Flags);
        }
        else
        {
            _hud.SetRegion(_regionName, _regionFlags);
        }
    }

    private void Fail(string message)
    {
        GD.PushError(message);
        _hud.ShowFatal(message);
    }
}
