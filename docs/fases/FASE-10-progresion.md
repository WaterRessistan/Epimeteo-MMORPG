# FASE 10 — Progresión

> Modelo: **Sonnet** (CLAUDE.md §6): implementación sobre diseño ya cerrado. El protocolo no sube
> de versión — todo lo que hace falta ya estaba reservado desde la Fase 1 salvo un hueco real
> (D5), y se cierra con el mismo criterio que `ShopRepair`/`LootTake` en las Fases 7 y 9.
>
> Prerrequisito leído: `docs/STATUS.md` (Fase 9 completa, en producción), `docs/02-esquema-bd.md`
> (`characters.level/xp/stat_points`, `character_skills`), `docs/01-protocolo.md` (`SkillCast`
> reservado sin tipar desde la Fase 1).

---

## 1. Objetivo

Que matar cosas signifique algo más allá del botín: subir de nivel, repartir puntos de stat y
desbloquear habilidades con maná y cooldown propios. Cierra además dos huecos que la Fase 9 dejó
anotados a propósito: los stats base de las clases eran "explícitamente provisionales" y el
combate usaba números fijos sin curva de por medio.

**Criterio de aceptación:** un personaje mata monstruos hasta subir de nivel, reparte los puntos
de stat que le tocan, lanza una habilidad de su clase contra un objetivo (con maná y cooldown de
verdad) y todo — nivel, XP, stats, maná gastado — sobrevive a un reinicio del servicio.

### Fuera de alcance, a propósito

- **Sin tope de nivel.** Ningún contenido depende todavía de un nivel máximo; ponerlo ahora sería
  una cifra inventada sin nada detrás. La curva de XP es abierta.
- **Las habilidades de curación sólo se apuntan a uno mismo.** Apuntar a un aliado exigiría
  decidir reglas de "quién puede curar a quién" (¿en grupo? ¿cualquiera?) que no pide esta fase.
- **Sin subida de nivel de habilidad** (`character_skills.skill_level` se queda siempre en 1,
  igual que `fertilizer_key`/`harvests_left` en la Fase 8: la columna está en el esquema cerrado,
  la lógica no es de esta fase).
- **Sin barra de vida/maná del objetivo con retrato.** El HUD ya tiene línea de combate desde la
  Fase 9; esta fase le añade la habilidad activa y su cooldown, no una pantalla nueva.

---

## 2. Las decisiones de diseño

### D1 — Curva de XP: pura, en `Shared`, simple a propósito

`LevelingFormulas.XpRequiredForNextLevel(nivel) = 100 × nivel` (100 para 1→2, 200 para 2→3...).
Lineal y no exponencial: con los premios de XP de la Fase 9 (8–20 por monstruo, provisionales
ellos también) una curva exponencial habría hecho ilegible cualquier prueba manual. Es la misma
filosofía que las fórmulas de daño de la Fase 9 — provisional a propósito, la reajusta quien
balancee el juego de verdad, lo que fija esta fase es la *forma*: pura, determinista, un número
exacto por nivel, no un rango.

### D2 — Subir de nivel se resuelve en el tick, nunca en la BD

Mismo criterio que granja (Fase 8 D1) y combate (Fase 9): `GameWorld` es la única fuente de
verdad, Postgres es un espejo asíncrono. Al ganar XP se comprueba en un bucle (por si una única
concesión cruzara más de un nivel, aunque hoy no pase) y cada nivel de más concede
`ProgressionConstants.StatPointsPerLevel` puntos de stat, recalcula HP/MP máximos y **cura del
todo** — subir de nivel nunca deja a nadie peor de lo que estaba.

### D3 — HP/MP máximos ahora escalan con el nivel; los stats base pasan a persistirse

`ClassDefinition` gana `HpPerLevel`/`MpPerLevel`. `InventorySystem.ComputeDerivedStats` gana un
parámetro `level`: `hpMax = classDef.BaseHp + classDef.HpPerLevel × (nivel − 1)`.

Hueco real cerrado de paso: `stat_str/int/vit/dex` y `stat_points` existen desde la Fase 2, se
leían al entrar y **nadie los escribía** — exactamente el mismo hueco que `hp/mp/xp/level` en la
Fase 9 (D12 de aquella fase), esta vez para los stats base en vez de los derivados. Con reparto de
puntos, un personaje perdería sus puntos gastados en cada reconexión si no se cierra ahora.
`CharacterSave` gana `StatStr/Int/Vit/Dex` y `StatPoints`.

### D4 — Un punto por mensaje, no un valor final

`C2SAllocateStatPoint { Stat }` gasta **un** punto en un stat cada vez — el cliente manda la
intención ("quiero un punto más en fuerza"), nunca el resultado (CLAUDE.md §4). Repartir varios
puntos de golpe es mandar el mensaje varias veces; no hace falta un mensaje de lote para esta fase.

### D5 — Hueco real: no había opcode para gastar un punto de stat

El catálogo original reservó `SkillCast` (`0x0061`) pero nada para stats. Se añade
**`AllocateStatPoint = 0x0063`**, siguiente hueco libre del bloque `0x006x` — mismo criterio que
`LootTake` en la Fase 9. Familia `Character` (mismo cupo que crear/borrar personaje: una acción de
menú, no de combate) pero estado legal `InWorld`, porque hace falta el personaje cargado para
saber qué clase es y qué stats tiene.

Segundo hueco, en `ResultCode`: 600–609 no cubre "no quedan puntos que gastar" ni "esa habilidad
no está desbloqueada todavía". Se añaden **`NoStatPointsAvailable = 610`** y
**`SkillNotUnlocked = 611`**, siguientes valores libres del bloque ya reservado.

### D6 — Las habilidades viven en `Shared`, no en `Server/Content`

A diferencia de `MonsterDefinition`/`CropDefinition` (servidor-only: el cliente no los necesita),
la barra de habilidades sí necesita saber qué habilidades tiene la clase, su coste de maná, su
cooldown y desde qué nivel — para pintarlas y para el cooldown visual. Mismo criterio que
`ItemCatalog`/`ShopCatalog` en las Fases 6–7: si el cliente lo necesita para pintar, va en
`Shared/Data`, no en `Server/Content`.

### D7 — Cooldown de habilidad, aparte del cooldown de ataque básico

Cada habilidad tiene su propio cooldown (`SkillCooldownMs` en el contenido), independiente del
cooldown de `Attack` que fijó la Fase 9. `PlayerEntity` gana un diccionario
`skillKey → instante en que vuelve a estar lista`, no un único cooldown global — lanzar una
habilidad no bloquea las demás ni el ataque básico.

### D8 — Daño y curación de habilidad: la misma fórmula de combate, con un extra plano

Ni las habilidades dañan por una fórmula nueva ni curan con una tirada aparte: `SkillDefinition`
declara `Power` (un bonus plano) y `Kind` (`Damage`/`Heal`, ambos ya reservados en
`CombatEventKind` desde la Fase 9, `Heal` sin usar hasta ahora). Un golpe de habilidad reutiliza
`CombatFormulas.Hit` con el ataque del lanzador **más** `Power` — dispersión y crítico incluidos,
igual que un ataque básico, pero con más pegada; una curación es `Power` en seco, sin RNG:
curar depende del contenido, no de la suerte. Inventar una segunda fórmula de daño (por ejemplo,
escalada por inteligencia para las clases mágicas) es un rediseño de combate que esta fase no
pide — queda anotado para cuando toque rebalancear de verdad.

### D9 — Curarse a uno mismo, sin más validación de objetivo que estar vivo

Con las curaciones sólo apuntando a uno mismo (§1), `SkillCast` de un `Kind.Heal` ignora el
`TargetEntityId` que mande el cliente y cura siempre al lanzador — más simple que inventar reglas
de "a quién se puede curar" y sin ninguna superficie de abuso: nadie puede curar a un enemigo por
error ni fingir curar a otro.

### D10 — `character_skills` no se toca esta fase

La tabla existe desde `docs/02` para "habilidades desbloqueadas". Aquí el desbloqueo es puro
contenido + nivel (¿tiene el personaje el nivel que pide la habilidad de su clase?), sin estado
que guardar: no hace falta una fila por desbloqueo si se puede recalcular en cada carga. Se deja
la tabla vacía y sin repositorio — igual que `world_state` sigue sin tocar desde la Fase 0.

---

## 3. Migración de BD

Ninguna. `stat_str/int/vit/dex`, `stat_points`, `level`, `xp` ya existen desde la Fase 2; sólo
faltaba escribirlos (D3).

## 4. `Shared`

| Fichero | Qué es |
|---|---|
| `Simulation/LevelingFormulas.cs` | `XpRequiredForNextLevel`, pura y testeada exacta (D1). |
| `Data/ProgressionConstants.cs` | Puntos de stat por nivel. |
| `Data/StatKind.cs` | `Str`/`Int`/`Vit`/`Dex`, para `AllocateStatPoint`. |
| `Data/SkillDefinition.cs`, `SkillLoader.cs`, `SkillCatalog.cs` | `content/skills/*.json`, mismo patrón validador que `ItemLoader` (D6). |
| `Net/Messages/C2SSkillCast.cs`, `C2SAllocateStatPoint.cs` | Tipados por primera vez (`SkillCast`) o nuevos (`AllocateStatPoint`, D5). |
| `Net/Opcode.cs` | `AllocateStatPoint = 0x0063` (D5). |
| `Net/ResultCode.cs` | `NoStatPointsAvailable = 610`, `SkillNotUnlocked = 611` (D5). |

## 5. Servidor

- `Content/ClassDefinition.cs`: gana `HpPerLevel`/`MpPerLevel`.
- `Inventory/InventorySystem.cs`: `ComputeDerivedStats` gana el parámetro `level` (D3).
- `World/PlayerEntity.cs`: gana `StatPoints` y `SkillCooldowns` (D7).
- `World/CharacterSave.cs` / `CharacterRepository.cs`: persisten stats base y puntos sin gastar.
- `Combat/LevelingSystem.cs` (nuevo, puro dado el estado): aplica una concesión de XP, con el
  bucle de niveles de más (D2).
- `Combat/SkillSystem.cs` (nuevo, puro): valida `SkillCast` (clase, nivel, maná, cooldown,
  alcance para daño) y resuelve el golpe o la curación (D8, D9) — mismo reparto Shared/Server que
  `CombatSystem`.
- `GameWorld`: handler de `SkillCast` y `AllocateStatPoint`; `GrantXp` pasa por `LevelingSystem`.

## 6. Contenido

`content/skills/*.json`: 3 por clase (guerrero, mago, híbrido — el híbrido con la única curación,
apuntada a uno mismo). Nivel de desbloqueo, coste de maná, cooldown y `Power`, todo provisional.

## 7. Cliente Godot

Sin arte. `NetClient` gana `SendSkillCast`/`SendAllocateStatPoint` y ya tenía `XpUpdateReceived`
(Fase 9, con `LeveledUp`/`XpToNextLevel` sin usar hasta ahora). Barra de habilidades: fila de
botones con las habilidades desbloqueadas de la clase, tecla numérica por hueco, cooldown visual
optimista (cuenta desde que se manda el mensaje, no desde la confirmación — el servidor decide de
verdad, esto es sólo para no dejar la barra sin respuesta). Panel de stats con los cuatro botones
de "+1" y los puntos que quedan.

## 8. Tests

`Shared.Tests`: `LevelingFormulasTests` (curva exacta), `SkillCatalogTests` si aplica validación
pura, o cubierto en `Server.Tests` si el catálogo vive ahí — ver D6, vive en Shared.

`Server.Tests`: `LevelingSystemTests` (una concesión de XP que no llega a subir de nivel; una que
sube exactamente uno; una que cruza dos niveles de golpe; los puntos de stat y el HP máximo suben
lo que toca; sube curado del todo), `SkillSystemTests` (lanzar sin maná, en cooldown, sin
desbloquear por nivel, contra un objetivo fuera de zona PvP igual que un ataque básico, una
curación que ignora el objetivo mandado y cura a quien la lanza), `AllocateStatPointTests` (sin
puntos disponibles, con clase desconocida).

## 9. Verificación sin Godot

`tools/Epimeteo.WorldBot --progression`: un bot mata monstruos hasta subir de nivel de verdad
(sin manipular nada por SQL — a diferencia de granja/tiendas, XP y nivel sí los produce el propio
protocolo), reparte los puntos de stat recibidos, compra... no, se equipa una herramienta ya
comprada en fases anteriores si hiciera falta, y lanza una habilidad de su clase contra un
monstruo (maná baja, cooldown impide repetir antes de tiempo). Reinicio del servicio de por medio
para comprobar que nivel, XP, stats y maná sobreviven.

## 10. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde.
2. Un personaje sube de nivel matando monstruos, cura del todo al subir, y gana los puntos de
   stat que le tocan.
3. Repartir un punto de stat sube el stat elegido y baja `statPoints`; sin puntos, se rechaza.
4. Lanzar una habilidad sin maná suficiente, en cooldown o sin el nivel que pide se rechaza sin
   cambiar nada; con todo en regla, baja el maná, aplica daño o curación y entra en su propio
   cooldown sin bloquear el ataque básico.
5. Nivel, XP, stats base y maná sobreviven un `systemctl restart epimeteo` real.
6. Cliente Godot compila sin warnings.

---

## 11. Resultado y hallazgos reales de la verificación E2E

Lo de arriba es el plan; esto es lo que pasó al ejecutarlo con
`tools/Epimeteo.WorldBot --progression-grind` / `--progression-verify` contra el servidor de
producción real. A diferencia de tiendas/granja, XP y nivel los produce el propio protocolo
matando monstruos de verdad — la única manipulación externa entre las dos corridas es un
`systemctl restart epimeteo` real, para probar que la persistencia sobrevive de verdad y no sólo
al vaciado de la cola de guardado.

### Dos fallos reales de servidor, no de la herramienta

- **`AllocateStatPoint` nunca llegaba al mundo.** D5 puso el opcode a propósito en
  `OpcodeFamily.Character` (mismo cupo que crear/borrar personaje, una acción de menú), pero
  `SessionMessageHandler.IsWorldFamily` — la lista de familias que cruzan a la cola del tick —
  nunca incluyó `Character`. Los otros cinco opcodes de esa familia (`CharListRequest`,
  `CharCreate`, `CharDelete`, `CharSelect`, `WorldReady`) tienen su propio `case` explícito antes
  de llegar a `IsWorldFamily` y nunca lo necesitaron; `AllocateStatPoint` no tiene uno —vive en
  `GameWorld`, se despacha por la cola— y caía directo en la rama de "opcode aún no implementado",
  que **expulsa la sesión** (`ProtocolError`). El primer intento de repartir un punto cerraba la
  conexión entera; los otros dos ni llegaban a mandarse. Se ve en el log del servidor real:
  `Opcode AllocateStatPoint aún no implementado (sesión N)`, y en `psql`, `stat_points`/`stat_vit`
  intactos pese a tres intentos. Arreglado añadiendo `OpcodeFamily.Character` a `IsWorldFamily`
  (`SessionMessageHandler.cs`) — no afecta a los otros cinco, que nunca llegan a ese método.
- **Subir de nivel concede puntos de stat que el cliente nunca se entera de tener.** `GrantXp`
  mandaba `BroadcastEntityStats` (HP/MP/nivel, Fase 9) al subir, pero `StatPoints` sólo viaja en
  `EquipmentUpdate` — que sólo se mandaba al equipar/desequipar. Un personaje podía subir de nivel,
  tener 3 puntos de verdad en el servidor y el cliente seguiría enseñando los que tenía antes,
  hasta el próximo cambio de equipo. Arreglado llamando también a `SendEquipmentUpdate(player)`
  dentro de `GrantXp` cuando `result.LeveledUp` (`GameWorld.cs`).

Los dos son huecos genuinos introducidos en esta misma fase, no anotados a propósito como los de
`docs/00` — los sacó a la luz sólo la verificación contra el servidor real; ni `dotnet test` ni la
lectura del código los habían tocado, porque nada en `Server.Tests` ejercita el enrutado de
`SessionMessageHandler` ni el camino completo de red de un `GrantXp`.

### Lo que resultó ser un fallo de la herramienta, no del juego

- **`KnownEntities` guarda la posición del `EntitySpawn`, no la de ahora.** El bot llevaba desde
  la Fase 7 usando esa posición para "ir hacia el monstruo más cercano", y hasta ahora nunca
  importaba (NPCs de tienda quietos). Con monstruos que patrullan o persiguen, apuntar a donde
  estaban al aparecer los deja indefinidamente fuera de alcance (`combat.OutOfRange`) en cuanto se
  mueven. Se añadió `Bot.LivePositions`, actualizado con cada `Snapshot` (no sólo la posición
  propia, que es lo único que mira el código de la Fase 4), y un `ApproachMonster` que va
  reapuntando a la posición fresca en vez de caminar una sola vez a un punto fijo.
- **Perseguir un imposible para siempre.** El campo tiene un muro de un tile (`x = 56`,
  `y = 18-29`, sin hueco visible en ese tramo) que separa la zona de los lobos de la de los limos.
  "El más cercano por distancia recta" a veces cae al otro lado: sin más criterio, el bot lo volvía
  a elegir en cuanto lo soltaba y se quedaba girando sobre el mismo objetivo inalcanzable el resto
  de la corrida. Se añadió una lista negra por corrida (se limpia si ya no queda nadie alcanzable
  a la vista, por si el bot se ha movido a otra parte del campo).
- **Quedarse quieto durante la espera del cooldown de la habilidad casi mata al bot.** La primera
  versión del guion mandaba `Run` sin más entre dos lanzamientos (3 s de cooldown): un lobo de
  verdad pega de vuelta y 3 s parado sin defenderse acumulan daño de sobra para un guerrero recién
  creado. Se cambió a seguir atacando con el ataque básico mientras se espera (D7: no comparte
  cooldown), que además demuestra mejor la independencia de los dos cooldowns que esperar de
  brazos cruzados.

### Un límite honesto de la verificación E2E

El criterio "subir de nivel cura del todo" lo fija exacto **`LevelingSystemTests`**, con estado
controlado: una concesión de XP, un personaje, sin nadie más pegando. El `--progression-grind`
prueba el camino entero contra el servidor real, pero el campo sigue lleno de monstruos mientras
se reparten los puntos de stat después de subir — un mordisco de fondo en los pocos ticks que tarda
el mensaje en volver dejó el HP en 129/134 en una corrida y 125/134 (tras el reinicio) en otra. No
es que no curara del todo: curó, y le pegaron justo después. El guion se ajustó a comprobar un
margen del 90 % en vez de la igualdad exacta — los dos juntos (unitario exacto + E2E con margen)
cubren el criterio; ninguno solo lo haría, mismo principio que el borde de la plaza en la Fase 9.

### Verificación real ejecutada

Tras los arreglos de arriba, **30/30 comprobaciones en verde** en la corrida final:

`--progression-grind` (23/23):
1. ✅ Empieza en nivel 1, sin XP ni puntos de stat.
2. ✅ Una habilidad de nivel 4 con nivel 1 se rechaza (`combat.SkillNotUnlocked`).
3. ✅ Golpe Poderoso hace daño de verdad; repetir enseguida topa con su propio cooldown
   (`combat.OnCooldown`) sin bloquear el ataque básico; con el maná justo para dos lanzamientos,
   el tercero topa con `combat.NotEnoughMana` sin golpear.
4. ✅ Sube de nivel matando monstruos de verdad (sin tocar SQL): `XpUpdate` trae `LeveledUp`, el
   HP y el MP máximos escalan exactamente con `HpPerLevel`/`MpPerLevel`, cura al subir (con el
   margen del apartado anterior), y concede los 3 puntos de `ProgressionConstants.StatPointsPerLevel`.
5. ✅ Repartir los 3 puntos sube VIT de 6 a 9 uno a uno, bajando `statPoints` cada vez; sin puntos
   ya, repartir uno más se rechaza (`progression.NoStatPointsAvailable`).

`--progression-verify`, tras `systemctl restart epimeteo` (7/7):
6. ✅ Nivel, MP máximo, MP guardado (lleno: nada lo gasta entre subir y desconectar), HP guardado
   (con un valor sano), puntos de stat gastados (0) y VIT (9) sobreviven el reinicio real. XP se
   queda por debajo del siguiente nivel, nunca en negativo.

400 tests en verde (155 `Shared` + 245 `Server`). Cliente Godot: compila sin warnings, con barra
de habilidades (teclas 1-3, cooldown visual optimista), panel de reparto de stats (tecla `K`) y
HUD de nivel/XP ampliado; como en las Fases 4–9, **no se ha ejecutado** — sigue sin haber Godot en
este servidor headless.
