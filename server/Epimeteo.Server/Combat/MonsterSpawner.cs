using Epimeteo.Server.Content;
using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.Combat;

/// <summary>
/// Mantiene poblados los puntos de aparición de un mapa (FASE-09 §2 D13): crea los monstruos que
/// falten y los repone cuando pasa su temporizador.
/// <para>
/// No persiste nada. Los monstruos son estado efímero: al arrancar el proceso, el mundo vuelve a
/// crearlos desde <c>content/</c>. Es lo que evita tener una tabla de monstruos que mantener al
/// día en cada tick.
/// </para>
/// </summary>
public sealed class MonsterSpawner
{
    private readonly MonsterCatalog _monsters;
    private readonly IReadOnlyList<MapSpawnPointDefinition> _points;
    private readonly int[] _alive;
    private readonly long[] _nextRespawnMs;

    public MonsterSpawner(MonsterCatalog monsters, GameMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        _monsters = monsters;
        _points = map.Spawns;
        _alive = new int[_points.Count];
        _nextRespawnMs = new long[_points.Count];
    }

    /// <summary>Puntos de aparición del mapa. Sólo para tests y diagnóstico.</summary>
    public int PointCount => _points.Count;

    /// <summary>
    /// Uno murió: baja la cuenta de su punto y programa la reposición. Se llama desde el tick, con
    /// el monstruo ya muerto pero todavía en el mundo.
    /// </summary>
    public void NotifyDeath(MonsterEntity monster, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(monster);

        var index = monster.SpawnPointIndex;
        if (index < 0 || index >= _points.Count)
        {
            return;
        }

        _alive[index] = Math.Max(0, _alive[index] - 1);
        _nextRespawnMs[index] = nowMs + (_points[index].RespawnSeconds * 1000L);
    }

    /// <summary>
    /// Crea los monstruos que falten en cada punto. Devuelve los nuevos para que quien llame los
    /// registre en la zona; no toca el mundo por su cuenta.
    /// </summary>
    public IReadOnlyList<MonsterEntity> Spawn(EntityIdAllocator ids, GameMap map, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(map);

        List<MonsterEntity>? spawned = null;

        for (var index = 0; index < _points.Count; index++)
        {
            var point = _points[index];

            if (_alive[index] >= point.Count || nowMs < _nextRespawnMs[index])
            {
                continue;
            }

            if (!_monsters.TryGet(point.MonsterKey, out var definition))
            {
                // Contenido incoherente: el catálogo ya falla ruidoso al arrancar si el JSON es
                // inválido, pero una clave que no existe sólo se ve aquí. No se tira el mundo
                // abajo por ello; simplemente ese punto se queda vacío.
                continue;
            }

            var home = new Vec2(point.X, point.Y);
            (spawned ??= []).Add(new MonsterEntity(ids.Next(), definition, home, point.Radius, index));
            _alive[index]++;
        }

        return spawned ?? [];
    }
}
