using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Simulation;

namespace Epimeteo.Server.Combat;

/// <summary>
/// Validación y aplicación de un golpe. Puro dado el mundo: no toca red, ni BD, ni reloj — se le
/// pasa todo lo que necesita saber. Mismo reparto que <c>InventorySystem</c>/<c>ShopSystem</c>/
/// <c>FarmSystem</c> en las fases anteriores, y por el mismo motivo: así las reglas de PvP se
/// pueden probar sin levantar un servidor, que es justo lo que pide el criterio de aceptación de
/// esta fase.
/// </summary>
public static class CombatSystem
{
    /// <summary>
    /// Las comprobaciones de <c>FASE-09 §2 D3</c>, en orden. Devuelve <see cref="ResultCode.Ok"/>
    /// si el golpe es legal.
    /// </summary>
    /// <param name="attacker">Quien pega.</param>
    /// <param name="target">A quién.</param>
    /// <param name="targetRangePos">
    /// Posición de la víctima <b>para resolver el alcance</b>: la rebobinada si hay compensación
    /// de latencia. Los flags de zona no se miran aquí sino en la posición actual de cada uno —
    /// la compensación mueve la geometría, nunca el permiso (D2).
    /// </param>
    /// <param name="map">Mapa, para regiones y línea de visión.</param>
    /// <param name="rangeTiles">Alcance del ataque.</param>
    /// <param name="requirePvpZone">
    /// Verdadero sólo si es jugador contra jugador: pegarse con monstruos es legal en cualquier
    /// región donde haya monstruos, y la plaza ya es <c>no_monsters</c>.
    /// </param>
    /// <param name="cooldownReady">Si el atacante ya cumplió su cooldown.</param>
    public static ResultCode ValidateAttack(
        WorldEntity attacker,
        WorldEntity target,
        Vec2 targetRangePos,
        GameMap map,
        float rangeTiles,
        bool requirePvpZone,
        bool cooldownReady)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(map);

        if (ReferenceEquals(attacker, target))
        {
            return ResultCode.CannotAttackTarget;
        }

        if (!target.IsAttackable)
        {
            return ResultCode.CannotAttackTarget;
        }

        if (!target.IsAlive)
        {
            return ResultCode.TargetDead;
        }

        if (!attacker.IsAlive)
        {
            return ResultCode.CannotAttackTarget;
        }

        if (requirePvpZone)
        {
            // D2: contra la posición **actual** de cada uno, no la rebobinada. Llegar a zona
            // segura protege en el acto; salir de ella expone en el acto. Si se rebobinara esto,
            // al cerrar el exploit de disparar desde la plaza se abriría el de matar a quien ya
            // está dentro.
            if (!map.Regions.Resolve(attacker.State.Pos).Flags.HasFlag(ZoneFlags.Pvp))
            {
                return ResultCode.SafeZone;
            }

            if (!map.Regions.Resolve(target.State.Pos).Flags.HasFlag(ZoneFlags.Pvp))
            {
                return ResultCode.TargetInSafeZone;
            }
        }

        if (!cooldownReady)
        {
            return ResultCode.OnCooldown;
        }

        if (!CombatFormulas.IsWithinRange(attacker.State.Pos, targetRangePos, rangeTiles))
        {
            return ResultCode.OutOfRange;
        }

        // Sin esto se pega a través de la muralla del pueblo. Se traza contra la posición que se
        // usó para el alcance, para que alcance y visión hablen del mismo cuerpo.
        if (!LineOfSight.IsClear(map.Collision, attacker.State.Pos, targetRangePos))
        {
            return ResultCode.OutOfRange;
        }

        return ResultCode.Ok;
    }

    /// <summary>
    /// Aplica un golpe ya validado: tira el daño y lo resta. Devuelve el resultado para poder
    /// mandarlo en <c>CombatEvent</c> y sumar amenaza.
    /// </summary>
    public static HitResult ApplyHit(WorldEntity attacker, WorldEntity target, DeterministicRng rng)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        var hit = CombatFormulas.Hit(attacker.CombatStats, target.CombatStats, rng);
        target.Hp = Math.Max(0, target.Hp - hit.Damage);
        return hit;
    }
}
