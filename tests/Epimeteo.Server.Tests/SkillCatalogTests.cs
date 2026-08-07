using Epimeteo.Server.Content;
using Epimeteo.Shared.Data;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Sin Postgres: valida el contenido real de <c>content/skills/</c> (FASE-10 §2 D6).</summary>
public sealed class SkillCatalogTests
{
    private static SkillCatalog LoadSkills() => new(ContentPaths.ResolveContentRoot());

    private static ClassCatalog LoadClasses() => new(ContentPaths.ResolveContentRoot());

    [Theory]
    [InlineData("class.warrior")]
    [InlineData("class.mage")]
    [InlineData("class.hybrid")]
    public void CadaClase_TieneAlMenosTresHabilidades(string classKey)
    {
        var skills = LoadSkills().ForClass(classKey).ToList();

        Assert.True(skills.Count >= 3, $"{classKey} sólo tiene {skills.Count} habilidades");
    }

    [Fact]
    public void TodasLasHabilidades_ReferencianClasesQueExisten()
    {
        var skills = LoadSkills();
        var classes = LoadClasses();

        foreach (var skill in skills.All)
        {
            Assert.True(classes.TryGet(skill.ClassKey, out _), $"{skill.Key} referencia la clase desconocida '{skill.ClassKey}'");
        }
    }

    /// <summary>Al menos una curación en todo el contenido, para que D9 (cura a uno mismo) tenga algo que probar de verdad.</summary>
    [Fact]
    public void HayAlMenosUnaCuracion()
    {
        Assert.Contains(LoadSkills().All, s => s.Kind == Epimeteo.Shared.Net.Messages.CombatEventKind.Heal);
    }

    [Fact]
    public void LasHabilidadesDeUnaClase_NoRepitenNivelDeDesbloqueo()
    {
        var skills = LoadSkills();

        foreach (var classDef in LoadClasses().All)
        {
            var niveles = skills.ForClass(classDef.Key).Select(s => s.RequiredLevel).ToList();
            Assert.Equal(niveles.Count, niveles.Distinct().Count());
        }
    }
}
