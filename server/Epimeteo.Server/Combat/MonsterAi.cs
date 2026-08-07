using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.Combat;

/// <summary>Lo que el monstruo quiere hacer este tick. Quien lo ejecuta es <c>Zone</c>.</summary>
/// <param name="AttackTargetId">Id de la víctima si quiere pegar, o <c>null</c>.</param>
public readonly record struct MonsterAction(int? AttackTargetId);

/// <summary>
/// La máquina de estados de los monstruos (FASE-09 §2 D8), aparte de la entidad para poder
/// probarla sin levantar un mundo: <c>Idle → Patrol → Chase → Attack → Returning</c>.
/// <para>
/// Decide y mueve, pero <b>no pega</b>: devuelve la intención y es <c>Zone</c> quien la pasa por
/// <see cref="CombatSystem"/>, igual que el input de un jugador. Así el monstruo está sujeto
/// exactamente a las mismas validaciones (alcance, línea de visión, cooldown) que cualquiera.
/// </para>
/// </summary>
public static class MonsterAi
{
    /// <summary>Cada cuánto vuelve a elegir destino de patrulla.</summary>
    private const int PatrolDecisionMs = 3000;

    /// <summary>Se considera que llegó a un punto cuando está a menos de esto.</summary>
    private const float ArriveToleranceTiles = 0.4f;

    /// <summary>Un tick de IA. <paramref name="candidates"/> son los jugadores de la zona.</summary>
    public static MonsterAction Tick(
        MonsterEntity monster,
        IReadOnlyList<PlayerEntity> candidates,
        GameMap map,
        DeterministicRng rng,
        long tick,
        long nowMs)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rng);

        if (!monster.IsAlive)
        {
            return default;
        }

        PurgeUnreachable(monster, candidates);

        // La correa se mira antes que nada: un monstruo lejos de casa vuelve, pase lo que pase.
        // Es lo que impide arrastrarlo hasta la plaza y soltarlo encima de alguien (D7).
        if (monster.AiState != MonsterState.Returning &&
            Vec2.DistanceSquared(monster.State.Pos, monster.Home) >
                monster.Definition.LeashRadiusTiles * monster.Definition.LeashRadiusTiles)
        {
            monster.Aggro.Clear();
            monster.AiState = MonsterState.Returning;
        }

        return monster.AiState switch
        {
            MonsterState.Returning => TickReturning(monster, map, tick),
            MonsterState.Chase or MonsterState.Attack => TickEngaged(monster, candidates, map, tick, nowMs),
            _ => TickPeaceful(monster, candidates, map, rng, tick, nowMs),
        };
    }

    /// <summary>Quita de la tabla a quien ya no puede estar en ella: muertos o fuera del mundo.</summary>
    private static void PurgeUnreachable(MonsterEntity monster, IReadOnlyList<PlayerEntity> candidates)
    {
        if (!monster.Aggro.Any)
        {
            return;
        }

        List<int>? drop = null;

        foreach (var (entityId, _) in monster.Aggro.Threat)
        {
            var player = FindById(candidates, entityId);
            if (player is null || !player.IsAlive || player.IsDead)
            {
                (drop ??= []).Add(entityId);
            }
        }

        foreach (var entityId in drop ?? [])
        {
            monster.Aggro.Remove(entityId);
        }
    }

    private static MonsterAction TickReturning(MonsterEntity monster, GameMap map, long tick)
    {
        if (Vec2.DistanceSquared(monster.State.Pos, monster.Home) <= ArriveToleranceTiles * ArriveToleranceTiles)
        {
            monster.ResetToHome(tick);
            return default;
        }

        StepToward(monster, monster.Home, map, tick);
        return default;
    }

    /// <summary>Persiguiendo o pegando: hay alguien en la tabla de amenaza.</summary>
    private static MonsterAction TickEngaged(
        MonsterEntity monster, IReadOnlyList<PlayerEntity> candidates, GameMap map, long tick, long nowMs)
    {
        var targetId = monster.Aggro.Top();
        var target = targetId is null ? null : FindById(candidates, targetId.Value);

        if (target is null)
        {
            monster.AiState = MonsterState.Returning;
            return default;
        }

        var distanceSquared = Vec2.DistanceSquared(monster.State.Pos, target.State.Pos);
        var range = monster.Definition.AttackRangeTiles;

        if (distanceSquared <= range * range)
        {
            monster.AiState = MonsterState.Attack;

            // Mira a su víctima aunque no se mueva: si no, pega de espaldas.
            monster.MoveTo(
                new MoveState(monster.State.Pos, Vec2.Zero, FacingToward(monster.State.Pos, target.State.Pos), AnimState.Idle),
                tick);

            var ready = nowMs - monster.LastAttackMs >= monster.Definition.AttackCooldownMs;
            return ready ? new MonsterAction(target.Id) : default;
        }

        monster.AiState = MonsterState.Chase;
        StepToward(monster, target.State.Pos, map, tick);
        return default;
    }

    /// <summary>Quieto o patrullando: busca a quién ver.</summary>
    private static MonsterAction TickPeaceful(
        MonsterEntity monster, IReadOnlyList<PlayerEntity> candidates, GameMap map,
        DeterministicRng rng, long tick, long nowMs)
    {
        var spotted = Spot(monster, candidates, map);
        if (spotted is not null)
        {
            // Entrar en la tabla con 1 de amenaza: quien luego pegue de verdad la superará
            // enseguida, que es lo que queremos (el que hace daño manda sobre el que sólo pasaba).
            monster.Aggro.Add(spotted.Id, 1);
            monster.AiState = MonsterState.Chase;
            return default;
        }

        if (nowMs >= monster.NextPatrolDecisionMs)
        {
            monster.NextPatrolDecisionMs = nowMs + PatrolDecisionMs;
            monster.PatrolTarget = RandomPointNearHome(monster, rng);
            monster.AiState = MonsterState.Patrol;
        }

        if (monster.AiState == MonsterState.Patrol)
        {
            if (Vec2.DistanceSquared(monster.State.Pos, monster.PatrolTarget) <= ArriveToleranceTiles * ArriveToleranceTiles)
            {
                monster.AiState = MonsterState.Idle;
                monster.MoveTo(new MoveState(monster.State.Pos, Vec2.Zero, monster.State.Facing, AnimState.Idle), tick);
            }
            else
            {
                StepToward(monster, monster.PatrolTarget, map, tick);
            }
        }

        return default;
    }

    /// <summary>
    /// A quién ve: el jugador vivo más cercano dentro del radio de aggro y con línea de visión.
    /// Un jugador al otro lado de un muro no despierta a nadie.
    /// </summary>
    private static PlayerEntity? Spot(MonsterEntity monster, IReadOnlyList<PlayerEntity> candidates, GameMap map)
    {
        PlayerEntity? best = null;
        var bestDistance = monster.Definition.AggroRadiusTiles * monster.Definition.AggroRadiusTiles;

        foreach (var player in candidates)
        {
            if (!player.IsAlive || player.IsDead)
            {
                continue;
            }

            var distance = Vec2.DistanceSquared(monster.State.Pos, player.State.Pos);
            if (distance > bestDistance)
            {
                continue;
            }

            if (!LineOfSight.IsClear(map.Collision, monster.State.Pos, player.State.Pos))
            {
                continue;
            }

            best = player;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// Un paso hacia un punto, eje a eje y con colisión, a la velocidad del monstruo. Es la misma
    /// idea que <see cref="MovementSystem.Step"/> pero con velocidad por definición de contenido
    /// en vez de la constante del jugador, así que no se puede reutilizar tal cual.
    /// </summary>
    private static void StepToward(MonsterEntity monster, Vec2 target, GameMap map, long tick)
    {
        var pos = monster.State.Pos;
        var dx = target.X - pos.X;
        var dy = target.Y - pos.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));

        if (length <= float.Epsilon)
        {
            return;
        }

        var step = monster.Definition.MoveSpeedTiles * SimulationConstants.TickDt;
        if (step > length)
        {
            step = length;
        }

        var moveX = dx / length * step;
        var moveY = dy / length * step;

        var next = pos;

        var candidateX = new Vec2(next.X + moveX, next.Y);
        if (!map.Collision.IsBlocked(candidateX, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight))
        {
            next = candidateX;
        }

        var candidateY = new Vec2(next.X, next.Y + moveY);
        if (!map.Collision.IsBlocked(candidateY, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight))
        {
            next = candidateY;
        }

        var moved = next != pos;

        monster.MoveTo(
            new MoveState(
                next,
                moved ? (next - pos) * SimulationConstants.InverseTickDt : Vec2.Zero,
                FacingToward(pos, target),
                moved ? AnimState.Walk : AnimState.Idle),
            tick);
    }

    private static Vec2 RandomPointNearHome(MonsterEntity monster, DeterministicRng rng)
    {
        var angle = rng.NextDouble() * Math.Tau;
        var distance = rng.NextDouble() * monster.Radius;

        return new Vec2(
            monster.Home.X + (float)(Math.Cos(angle) * distance),
            monster.Home.Y + (float)(Math.Sin(angle) * distance));
    }

    private static Facing FacingToward(Vec2 from, Vec2 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            return dx < 0 ? Facing.West : Facing.East;
        }

        return dy < 0 ? Facing.North : Facing.South;
    }

    private static PlayerEntity? FindById(IReadOnlyList<PlayerEntity> candidates, int entityId)
    {
        foreach (var player in candidates)
        {
            if (player.Id == entityId)
            {
                return player;
            }
        }

        return null;
    }
}
