using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.Tests;

/// <summary>Ayudas para montar mundos de prueba sin tocar disco, red ni Postgres.</summary>
internal static class TestWorld
{
    /// <summary>Mapa abierto con muro perimetral, del tamaño que pida el test.</summary>
    public static GameMap Map(int width = 96, int height = 96, params MapRegionDefinition[] regions)
    {
        var rows = new string[height];
        for (var y = 0; y < height; y++)
        {
            rows[y] = y == 0 || y == height - 1
                ? new string('#', width)
                : "#" + new string('.', width - 2) + "#";
        }

        return MapLoader.Compile(
            new MapDefinition
            {
                Key = "map.test",
                DisplayName = "Prueba",
                Width = width,
                Height = height,
                Spawn = new MapSpawnDefinition { X = width / 2f, Y = height / 2f, Facing = 2 },
                Collision = rows,
                Regions = regions,
            },
            "test");
    }

    /// <summary>Petición de entrada con valores razonables; el test cambia lo que le interese.</summary>
    public static WorldJoinRequest Join(
        int entityId, Vec2 position, long characterId = 0, long gold = 0, bool isAdmin = false, string username = "") => new(
        entityId,
        characterId == 0 ? entityId : characterId,
        $"Jugador{entityId}",
        "class.warrior",
        "map.test",
        position,
        Facing.South,
        PaletteIndex: 0,
        Hp: 100,
        HpMax: 120,
        Mp: 50,
        MpMax: 50,
        StatStr: 8,
        StatInt: 2,
        StatVit: 6,
        StatDex: 4,
        Items: [],
        Gold: gold,
        Level: 1,
        Xp: 0,
        StatPoints: 0, AccountId: entityId, IsAdmin: isAdmin, Username: username);

    /// <summary>Input de movimiento en una dirección.</summary>
    public static MoveInput Input(uint seq, int dirX, int dirY) =>
        new(seq, (sbyte)dirX, (sbyte)dirY, dirY < 0 ? Facing.North : Facing.South);

    /// <summary>Mete inputs y simula <paramref name="ticks"/> ticks moviendo al jugador.</summary>
    public static void Walk(Zone zone, FakeWorldPeer peer, int dirX, int dirY, int ticks, ref uint seq, ref long tick)
    {
        for (var i = 0; i < ticks; i++)
        {
            zone.EnqueueInput(peer.Id, Input(seq++, dirX, dirY), tick * 50);
            zone.Tick(++tick, tick * 50);
        }
    }

    /// <summary>Ids que aparecen en los <c>EntitySpawn</c> recibidos.</summary>
    public static IEnumerable<int> SpawnedIds(FakeWorldPeer peer) => peer
        .Messages<S2CEntitySpawn>(Shared.Net.Opcode.EntitySpawn)
        .SelectMany(message => message.Entities)
        .Select(entity => entity.Id);

    /// <summary>Ids que aparecen en los <c>EntityDespawn</c> recibidos.</summary>
    public static IEnumerable<EntityDespawnEntry> DespawnedIds(FakeWorldPeer peer) => peer
        .Messages<S2CEntityDespawn>(Shared.Net.Opcode.EntityDespawn)
        .SelectMany(message => message.Entities);
}
