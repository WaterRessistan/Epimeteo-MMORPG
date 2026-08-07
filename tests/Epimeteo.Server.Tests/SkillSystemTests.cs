using Epimeteo.Server.Combat;
using Epimeteo.Server.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Validación de <c>SkillCast</c> (FASE-10 §2 D7): nivel, maná y cooldown propio, sin red ni BD.</summary>
public sealed class SkillSystemTests
{
    private static SkillDefinition Skill(int requiredLevel = 1, int manaCost = 10, int cooldownMs = 5000) => new()
    {
        Key = "skill.test",
        DisplayName = "Prueba",
        ClassKey = "class.warrior",
        RequiredLevel = requiredLevel,
        ManaCost = manaCost,
        CooldownMs = cooldownMs,
        Kind = CombatEventKind.Damage,
        Power = 10,
        RangeTiles = 1.5f,
    };

    private static PlayerEntity Caster(int level = 1, int mp = 50) => new(
        1, new FakeWorldPeer(1), characterId: 1, "class.warrior", "J",
        MoveState.AtRest(new Vec2(5, 5), Facing.South), nowMs: 0, items: [])
    {
        Level = level,
        Mp = mp,
        MpMax = 50,
    };

    [Fact]
    public void ConNivelSuficienteManaYSinCooldown_Vale()
    {
        var caster = Caster(level: 5, mp: 20);

        Assert.Equal(ResultCode.Ok, SkillSystem.ValidateCast(caster, Skill(requiredLevel: 3, manaCost: 10), nowMs: 0));
    }

    [Fact]
    public void SinElNivelQueExige_SkillNotUnlocked()
    {
        var caster = Caster(level: 2);

        Assert.Equal(ResultCode.SkillNotUnlocked, SkillSystem.ValidateCast(caster, Skill(requiredLevel: 5), nowMs: 0));
    }

    [Fact]
    public void SinManaSuficiente_NotEnoughMana()
    {
        var caster = Caster(mp: 5);

        Assert.Equal(ResultCode.NotEnoughMana, SkillSystem.ValidateCast(caster, Skill(manaCost: 10), nowMs: 0));
    }

    [Fact]
    public void EnCooldownPropio_OnCooldown()
    {
        var caster = Caster();
        var skill = Skill(cooldownMs: 5000);
        caster.SkillCooldowns[skill.Key] = 10_000;

        Assert.Equal(ResultCode.OnCooldown, SkillSystem.ValidateCast(caster, skill, nowMs: 5000));
    }

    [Fact]
    public void TrasExpirarElCooldownPropio_VuelveAValer()
    {
        var caster = Caster();
        var skill = Skill(cooldownMs: 5000);
        caster.SkillCooldowns[skill.Key] = 10_000;

        Assert.Equal(ResultCode.Ok, SkillSystem.ValidateCast(caster, skill, nowMs: 10_000));
    }

    /// <summary>
    /// El cooldown de una habilidad es suyo: que el ataque básico esté listo o no no influye
    /// (D7) — este test sólo documenta que <c>ValidateCast</c> no mira <c>LastAttackMs</c> en
    /// absoluto, a propósito.
    /// </summary>
    [Fact]
    public void ElCooldownDeAtaqueBasico_NoAfectaAlDeLaHabilidad()
    {
        var caster = Caster();
        caster.LastAttackMs = 999_999; // "acaba de atacar"

        Assert.Equal(ResultCode.Ok, SkillSystem.ValidateCast(caster, Skill(), nowMs: 1_000_000));
    }

    // ── CombatSystem.ApplyHit con bonus de habilidad (D8) ───────────────

    [Fact]
    public void ApplyHit_ConBonusDeHabilidad_PegaMasFuerteQueSinEl()
    {
        var attacker = Caster();
        attacker.AttackPower = 20;
        var target1 = Caster(); target1.Hp = 1000; target1.HpMax = 1000; target1.Defense = 0;
        var target2 = Caster(); target2.Hp = 1000; target2.HpMax = 1000; target2.Defense = 0;

        var sinBonus = CombatSystem.ApplyHit(attacker, target1, new DeterministicRng(1), powerBonus: 0);
        var conBonus = CombatSystem.ApplyHit(attacker, target2, new DeterministicRng(1), powerBonus: 30);

        Assert.True(conBonus.Damage > sinBonus.Damage);
    }
}
