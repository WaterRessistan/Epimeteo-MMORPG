using Epimeteo.Server.Content;
using Epimeteo.Shared.Data;

namespace Epimeteo.ContentValidator;

/// <summary>
/// Las referencias entre ficheros de <c>content/</c> que ningún catálogo comprueba por sí solo
/// (FASE-12 §2 D4): cada uno valida su propia forma al cargar, pero nada mira si la <c>defKey</c>
/// que escribió otro fichero existe de verdad. Puro — sólo lee los catálogos ya cargados — para
/// poder probarlo sin tocar disco si hiciera falta.
/// </summary>
internal static class ContentCrossChecker
{
    public static IEnumerable<string> Check(
        ItemCatalog items, ClassCatalog classes, MonsterCatalog monsters, CropCatalog crops,
        ShopCatalog shops, SkillCatalog skills, MapCatalog maps)
    {
        foreach (var classDef in classes.All)
        {
            foreach (var starting in classDef.StartingItems)
            {
                if (!items.TryGet(starting.DefKey, out _))
                {
                    yield return $"class.{classDef.Key}: el kit inicial referencia el ítem '{starting.DefKey}', que no existe.";
                }
            }
        }

        foreach (var monster in monsters.All)
        {
            foreach (var loot in monster.Loot)
            {
                if (!items.TryGet(loot.DefKey, out _))
                {
                    yield return $"monster.{monster.Key}: el botín referencia el ítem '{loot.DefKey}', que no existe.";
                }
            }
        }

        foreach (var shop in shops.All)
        {
            foreach (var slot in shop.Items)
            {
                if (!items.TryGet(slot.DefKey, out _))
                {
                    yield return $"shop.{shop.Key}: un hueco referencia el ítem '{slot.DefKey}', que no existe.";
                }
            }

            if (!maps.TryGet(shop.Npc.MapKey, out _))
            {
                yield return $"shop.{shop.Key}: el tendero está en el mapa '{shop.Npc.MapKey}', que no existe.";
            }
        }

        foreach (var crop in crops.All)
        {
            if (!items.TryGet(crop.SeedDefKey, out _))
            {
                yield return $"crop.{crop.Key}: la semilla referencia el ítem '{crop.SeedDefKey}', que no existe.";
            }

            if (!items.TryGet(crop.YieldDefKey, out _))
            {
                yield return $"crop.{crop.Key}: la cosecha referencia el ítem '{crop.YieldDefKey}', que no existe.";
            }
        }

        foreach (var skill in skills.All)
        {
            if (!classes.TryGet(skill.ClassKey, out _))
            {
                yield return $"skill.{skill.Key}: referencia la clase '{skill.ClassKey}', que no existe.";
            }
        }

        foreach (var map in maps.All)
        {
            foreach (var spawn in map.Spawns)
            {
                if (!monsters.TryGet(spawn.MonsterKey, out _))
                {
                    yield return $"map.{map.Key}: un punto de spawn referencia el monstruo '{spawn.MonsterKey}', que no existe.";
                }
            }
        }
    }
}
