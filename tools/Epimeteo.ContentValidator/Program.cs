using Epimeteo.Server.Content;
using Epimeteo.Server.Shop;
using Epimeteo.Shared.Data;

namespace Epimeteo.ContentValidator;

/// <summary>
/// Carga <c>content/</c> con los mismos catálogos que usa el servidor de verdad al arrancar y
/// comprueba las referencias cruzadas que ningún catálogo por sí solo puede validar: cada uno se
/// valida a sí mismo (una tienda con una <c>defKey</c> que no existe no lo sabe, porque
/// <c>ShopCatalog</c> nunca llega a mirar <c>ItemCatalog</c>). FASE-12 §2 D4.
/// <code>dotnet run --project tools/Epimeteo.ContentValidator -- [ruta a content/]</code>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var contentRoot = args.Length > 0 ? args[0] : ContentPaths.ResolveContentRoot();
        Console.WriteLine($"content/ : {contentRoot}");

        ItemCatalog items;
        ClassCatalog classes;
        MonsterCatalog monsters;
        CropCatalog crops;
        ShopCatalog shops;
        SkillCatalog skills;
        MapCatalog maps;

        try
        {
            items = new ItemCatalog(contentRoot);
            classes = new ClassCatalog(contentRoot);
            monsters = new MonsterCatalog(contentRoot);
            crops = new CropCatalog(contentRoot);
            shops = new ShopCatalog(contentRoot);
            skills = new SkillCatalog(contentRoot);
            maps = new MapCatalog(contentRoot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            // Cada catálogo ya valida su propio JSON al cargar (mismo criterio que el arranque
            // real del servidor, CLAUDE.md §4): si alguno falla, no hay nada más que comprobar.
            Console.WriteLine($"  [MAL] {ex.Message}");
            return 1;
        }

        Console.WriteLine(
            $"  [OK ] {items.All.Count} ítems, {classes.All.Count} clases, {monsters.All.Count} monstruos, " +
            $"{crops.All.Count} cultivos, {shops.All.Count} tiendas, {skills.All.Count} habilidades, {maps.All.Count} mapas");

        var problems = ContentCrossChecker.Check(items, classes, monsters, crops, shops, skills, maps).ToList();

        if (problems.Count == 0)
        {
            Console.WriteLine("\nTodas las referencias cruzadas resuelven. 0 problemas.");
            return 0;
        }

        Console.WriteLine($"\n{problems.Count} problema(s):");
        foreach (var problem in problems)
        {
            Console.WriteLine($"  [MAL] {problem}");
        }

        return 1;
    }
}
