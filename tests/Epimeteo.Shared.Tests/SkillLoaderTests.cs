using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>Validación pura de <c>content/skills/*.json</c> (FASE-10 §2 D6), sin tocar disco.</summary>
public sealed class SkillLoaderTests
{
    private const string Valida = """
        {
          "key": "skill.test_slash",
          "displayName": "Prueba",
          "classKey": "class.warrior",
          "requiredLevel": 3,
          "manaCost": 10,
          "cooldownMs": 4000,
          "kind": "Damage",
          "power": 15,
          "rangeTiles": 1.5
        }
        """;

    [Fact]
    public void JsonValido_SeParseaEntero()
    {
        var skill = SkillLoader.Parse(Valida, "test");

        Assert.Equal("skill.test_slash", skill.Key);
        Assert.Equal("class.warrior", skill.ClassKey);
        Assert.Equal(3, skill.RequiredLevel);
        Assert.Equal(10, skill.ManaCost);
        Assert.Equal(4000, skill.CooldownMs);
        Assert.Equal(CombatEventKind.Damage, skill.Kind);
        Assert.Equal(15, skill.Power);
        Assert.Equal(1.5f, skill.RangeTiles);
    }

    [Fact]
    public void SinKey_Falla()
    {
        var json = Valida.Replace("\"skill.test_slash\"", "null");
        Assert.Throws<InvalidOperationException>(() => SkillLoader.Parse(json, "test"));
    }

    [Fact]
    public void SinClassKey_Falla()
    {
        var json = Valida.Replace("\"class.warrior\"", "null");
        Assert.Throws<InvalidOperationException>(() => SkillLoader.Parse(json, "test"));
    }

    [Theory]
    [InlineData("\"requiredLevel\": 3", "\"requiredLevel\": 0")]
    [InlineData("\"manaCost\": 10", "\"manaCost\": -1")]
    [InlineData("\"cooldownMs\": 4000", "\"cooldownMs\": 0")]
    [InlineData("\"power\": 15", "\"power\": 0")]
    public void ConValorImposible_Falla(string original, string reemplazo)
    {
        var json = Valida.Replace(original, reemplazo);
        Assert.Throws<InvalidOperationException>(() => SkillLoader.Parse(json, "test"));
    }

    [Fact]
    public void KindDesconocido_Falla()
    {
        var json = Valida.Replace("\"Damage\"", "\"Fireball\"");
        Assert.Throws<InvalidOperationException>(() => SkillLoader.Parse(json, "test"));
    }

    [Fact]
    public void DañoSinAlcancePositivo_Falla()
    {
        var json = Valida.Replace("\"rangeTiles\": 1.5", "\"rangeTiles\": 0");
        Assert.Throws<InvalidOperationException>(() => SkillLoader.Parse(json, "test"));
    }

    /// <summary>Una curación no necesita alcance: siempre se apunta a uno mismo (D9).</summary>
    [Fact]
    public void CuracionSinAlcance_NoFalla()
    {
        var json = Valida.Replace("\"Damage\"", "\"Heal\"").Replace("\"rangeTiles\": 1.5", "\"rangeTiles\": 0");

        var skill = SkillLoader.Parse(json, "test");

        Assert.Equal(CombatEventKind.Heal, skill.Kind);
    }

    [Fact]
    public void SinDisplayName_UsaLaClave()
    {
        var json = Valida.Replace("\"displayName\": \"Prueba\",", string.Empty);

        var skill = SkillLoader.Parse(json, "test");

        Assert.Equal("skill.test_slash", skill.DisplayName);
    }
}
