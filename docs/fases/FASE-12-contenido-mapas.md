# FASE 12 — Pipeline de contenido y mapas

> Modelo: **Sonnet** (CLAUDE.md §6): implementación sobre diseño ya cerrado.

## 1. Objetivo y alcance real

El roadmap (`docs/03`) pide cuatro cosas: integrar packs CC0 de verdad, un atlas registry, el
mapa del pueblo "completo" más 2 zonas exteriores, y `tools/Epimeteo.ContentValidator`.

**La primera choca de frente con CLAUDE.md §5: "Nunca generes ni descargues assets sin que se
pida explícitamente."** Preguntado explícitamente al usuario al empezar esta fase, la respuesta
fue: sólo la infraestructura. Esta fase construye todo lo que no depende de tener arte de verdad
— el atlas registry, el validador, los mapas nuevos (datos de colisión y regiones, sin sprites) —
y deja `client/assets/ATTRIBUTIONS.md` como plantilla vacía, lista para cuando alguien traiga
packs reales. Ninguna imagen se genera ni se descarga esta sesión, ni siquiera placeholders de
`_ai_placeholder/` (CLAUDE.md §5 los prevé, pero generarlos también es "generar assets").

### Fuera de alcance, a propósito

- **Ningún asset real.** Ver arriba.
- **Tiled / TileMapLayer de Godot.** Es autoría visual sobre un tileset real; sin tileset no hay
  nada que autorar ahí. El mapa "completo" de esta fase es igual de sin-arte que los anteriores
  (colisión ASCII + regiones), sólo que hay dos más.
- **Viajar entre zonas.** Las dos zonas exteriores nuevas son mapas de verdad, cargados y
  simulados igual que `map.village` (`GameWorld` ya crea una `Zone` por cada `MapCatalog.All`,
  genérico desde la Fase 4) — pero no hay ningún portal ni borde que lleve a un personaje de una a
  otra. Sin eso, "personaje entra en `map.forest`" sólo se puede probar colocándolo ahí a mano por
  SQL, exactamente igual que adelantar el reloj de granja en la Fase 8. Un sistema de transición
  de verdad es su propia pieza, no cabe aquí sin desbordar la fase.

## 2. Decisiones de diseño

### D1 — El atlas registry vive en `client/assets/`, no en `content/`

`content/*.json` es contenido de juego compartido por servidor y cliente (CLAUDE.md §3); el mapeo
`clave visual → región de atlas` no lo necesita el servidor para nada — "el servidor no conoce
assets" es literal en CLAUDE.md §5. `client/assets/atlas_registry.json` es un array de
`{ key, atlasPath, x, y, width, height }`. Vacío por ahora (sin arte real), lista de tests.

### D2 — Los ítems ganan `visualKey`, las entidades reutilizan `defKey`

Los ítems pueden querer compartir sprite entre variantes ("espada de hierro" y "espada de acero"
con el mismo dibujo hasta que haya arte distinto) — de ahí un campo `visualKey` aparte en
`ItemDefinition`, opcional y con `Key` como valor por defecto (no rompe los 10 JSON existentes).
Jugadores, monstruos y NPCs no tienen ese caso todavía: cada `defKey` ya es 1:1 con su aspecto, así
que el registro los busca directamente por `defKey` — no hace falta un campo nuevo en
`MonsterDefinition`/`ClassDefinition` (que además son servidor-only, Fase 9 D6) sólo para
duplicar lo que `defKey` ya da.

### D3 — Resolución con caída al placeholder actual, nunca una ruta hardcodeada

`AtlasRegistry.TryGet(key)` devuelve la región si existe **y** el fichero de verdad está en disco
(`ResourceLoader.Exists`, Godot); si cualquiera de las dos cosas falta —hoy, siempre— el render
sigue exactamente como hasta ahora: el rectángulo de color por paleta. Ni un solo `if` en
`WorldRenderer` menciona un nombre de fichero: la clave sale de `defKey`/`visualKey`, la ruta sale
del registro.

### D4 — El validador reutiliza los catálogos que ya existen, no reinventa el parseo

`tools/Epimeteo.ContentValidator` no vuelve a escribir lectura de JSON: instancia
`ItemCatalog`/`ClassCatalog`/`MonsterCatalog`/`CropCatalog`/`ShopCatalog`/`SkillCatalog`/
`MapCatalog` de verdad (los mismos que carga el servidor al arrancar) y, si construirlos no
lanzó, recorre las referencias cruzadas que ningún catálogo comprueba hoy porque cada uno sólo se
valida a sí mismo: ítems iniciales de una clase, botín de un monstruo, slots de una tienda, semilla
de un cultivo, `classKey` de una habilidad, `monsterKey` de un punto de spawn. Sale con código 0
si todo resuelve, 1 y una lista si algo no.

### D5 — Dos zonas exteriores nuevas, más pequeñas que el pueblo

`map.forest` y `map.mountain` (48×48, frente a los 96×96 de `map.village`): un muro perimetral,
una región `pvp` que cubre casi todo el interior (mismo criterio que `campo_norte`) y un par de
puntos de monstruos cada una, reutilizando `monster.slime`/`monster.wolf` — no hace falta
contenido de monstruo nuevo para demostrar que el mapa funciona.

## 3. Migración de BD

Ninguna.

## 4. `Shared`

| Fichero | Qué es |
|---|---|
| `Data/AtlasRegion.cs` | Una entrada del registro: clave, ruta, rectángulo. |
| `Data/AtlasRegistryLoader.cs` | Parsea `atlas_registry.json`, puro y testeado (D1). |
| `Data/AtlasRegistry.cs` | Envoltorio consultable, mismo patrón que `SkillCatalog`. |
| `Data/ItemDefinition.cs` | Gana `VisualKey` (D2). |
| `Data/ItemLoader.cs` | Lee `visualKey`, por defecto `key`. |

## 5. Cliente Godot

`WorldRenderer` construye un `AtlasRegistry` al cargar el mapa y lo consulta por `defKey` (o
`visualKey` para lo que sea un ítem) antes de dibujar el rectángulo de siempre (D3). Sin pantalla
nueva: es un cambio dentro de `DrawEntity`.

## 6. Herramienta nueva

`tools/Epimeteo.ContentValidator`: consola, referencia `Epimeteo.Shared` y `Epimeteo.Server` (para
los catálogos servidor-only). `dotnet run --project tools/Epimeteo.ContentValidator [ruta a content/]`.

## 7. Contenido

- `content/maps/map.forest.json`, `content/maps/map.mountain.json` (D5).
- Los 10 `content/items/*.json` no cambian (campo opcional, D2) — se deja anotado que **no** se
  ha rellenado `visualKey` en ninguno a propósito: sin atlas real todavía no hay nada que apuntar.

## 8. Tests

`Shared.Tests`: `AtlasRegistryLoaderTests` (parseo puro: duplicados, dimensiones inválidas, JSON
vacío válido). `Server.Tests`: las dos zonas nuevas cargan con `MapCatalog` real y tienen la
región `pvp` esperada (mismo criterio que `ElMapaDelPueblo_SeCargaYTieneSuZona`, Fase 9).

## 9. Verificación sin Godot

`dotnet run --project tools/Epimeteo.ContentValidator -- content` contra el `content/` real, en
local y desplegado. `/status` en producción confirma `world.zones` subido en 2. Los logs del
arranque confirman que los dos `MonsterSpawner` nuevos poblaron sus puntos (mismo log que
`campo_norte` desde la Fase 9). Sin bot nuevo: no hay forma de que un personaje llegue a las
zonas nuevas sin un sistema de transición (§1, fuera de alcance), así que no hay nada que un bot
pueda hacer ahí que un test de `MapCatalog` no cubra ya.

## 10. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde.
2. `ContentValidator` sale con código 0 contra el `content/` real, y con código 1 y un mensaje
   claro si se le rompe una referencia a propósito (prueba manual, no se deja el content roto).
3. Las dos zonas nuevas cargan, con colisión y región `pvp`, y sus monstruos aparecen solos.
4. El atlas registry resuelve una clave presente en el manifiesto y devuelve `null` en una
   ausente, sin romper el render actual (sigue sin haber ni un sprite).
5. Cliente Godot compila sin warnings.

---

## 11. Resultado y hallazgos reales

Lo de arriba es el plan; esto es lo que pasó al implementarlo y verificarlo contra producción.

### Un hallazgo real, en los tests, no en el servidor

Añadir `map.forest`/`map.mountain` rompió tres tests de `WorldTests.cs` que llevaban en verde
desde la Fase 4 (`UnJoin_ConservaElOroGuardado`, `AlGuardar_ViajanVidaYExperiencia`,
`UnInputState_MueveLaEntidadAutoritativa`): los tres hacían `world.Zones.First().FindBySession(1)`
para encontrar al jugador de prueba, asumiendo implícitamente que sólo había una zona — cierto
desde la Fase 4 hasta esta misma fase. Con tres mapas, `.First()` deja de apuntar necesariamente a
`map.village` (el orden de `Directory.EnumerateFiles` no está garantizado) y el jugador —que sí
entra en `map.village`, vía `VillageJoin`— podía no estar en la zona que devolvía `.First()`, dando
`FindBySession(1) == null`. No es un fallo de `GameWorld` ni de la carga de mapas: es la clase de
suposición que sólo se nota cuando deja de ser cierta, y es exactamente lo que esta fase cambiaba a
propósito. Arreglado buscando la zona por `Map.Key == "map.village"` en los tres, en vez de por
posición en la colección.

### Todo lo demás, sin sorpresas

El resto salió tal como estaba escrito en el plan: el atlas registry resuelve por clave y cae al
rectángulo de siempre sin que nadie note la diferencia (no hay nada que resolver, con el
manifiesto vacío a propósito), `ContentValidator` encontró 0 problemas en el `content/` real y
detectó correctamente una referencia rota cuando se le rompió una a mano para probarlo, y las dos
zonas nuevas se cargan y se pueblan de monstruos solas, igual que `campo_norte` desde la Fase 9.

### Verificación real ejecutada

1. ✅ `dotnet build` sin warnings; `dotnet test`: **163/163 compartidos + 276/276 servidor**.
2. ✅ `dotnet build client/Epimeteo.Client.csproj`: sin warnings. UI no ejercitada a mano.
3. ✅ `tools/Epimeteo.ContentValidator -- content`: 0 problemas contra el `content/` real (10
   ítems, 3 clases, 2 monstruos, 1 cultivo, 2 tiendas, 9 habilidades, 3 mapas). Con una `defKey`
   rota a mano en `class.warrior`, sale con código 1 y el mensaje exacto de qué referencia qué;
   restaurado con `git checkout` antes de seguir.
4. ✅ Desplegado con `deploy/publish.sh`. `tools/Epimeteo.ContentValidator -- /opt/epimeteo/content`
   contra el `content/` sincronizado de verdad: también 0 problemas.
5. ✅ `/status` en producción: `world.zones = 3` (antes 1), `world.monsters = 12` — exactamente los
   6 de siempre en `map.village` más 3 en cada zona nueva (2 limos + 1 lobo por zona), confirmando
   que los `MonsterSpawner` de las dos zonas nuevas poblaron sus puntos solos, sin que nadie las
   visitara nunca (no hay sistema de transición, §1).

**Límite honesto, el mismo que ya anotaba §9 del plan:** sin un sistema de transición entre mapas,
ningún personaje de verdad puede llegar a `map.forest`/`map.mountain` todavía — se comprueba que
la zona existe, carga y se puebla (`Server.Tests` con `MapCatalog` real, más `/status` en
producción), no que un jugador pueda visitarla. Eso es contenido para cuando exista la pieza que
falta, no algo que quepa forzar esta fase sin desbordarla.
