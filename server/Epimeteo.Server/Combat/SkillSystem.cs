using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Combat;

/// <summary>
/// Validación de <c>SkillCast</c> — puro, sin I/O, mismo espíritu que
/// <c>CombatSystem.ValidateAttack</c> (FASE-10 §2 D7). No aplica el golpe ni la curación: eso lo
/// hace <c>GameWorld</c> reutilizando <c>CombatSystem.ApplyHit</c> (daño) o sumando
/// <see cref="SkillDefinition.Power"/> directamente (curación, sin RNG — D8/D9).
/// </summary>
public static class SkillSystem
{
    /// <summary>
    /// Nivel, maná y cooldown propio de la habilidad (D7: aparte del cooldown de <c>Attack</c>).
    /// La zona/alcance/línea de visión de una habilidad de daño las valida
    /// <c>CombatSystem.ValidateAttack</c> aparte — son las mismas reglas que un ataque básico.
    /// </summary>
    public static ResultCode ValidateCast(PlayerEntity caster, SkillDefinition skill, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(skill);

        if (caster.Level < skill.RequiredLevel)
        {
            return ResultCode.SkillNotUnlocked;
        }

        if (caster.Mp < skill.ManaCost)
        {
            return ResultCode.NotEnoughMana;
        }

        if (nowMs < caster.SkillCooldowns.GetValueOrDefault(skill.Key))
        {
            return ResultCode.OnCooldown;
        }

        return ResultCode.Ok;
    }
}
