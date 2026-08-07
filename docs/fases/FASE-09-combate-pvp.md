# FASE 09 — Combate, monstruos y PvP

> Modelo: **Opus** (CLAUDE.md §6): netcode con compensación de latencia, reglas de PvP y
> anticheat. Es la primera fase desde la 4 en la que el protocolo **sube de versión**.
>
> Prerrequisito leído: `docs/STATUS.md` (Fase 8 completa, en producción),
> `docs/00-arquitectura.md §6` (zonas y PvP, lag compensation), `docs/01-protocolo.md`
> (opcodes de combate ya reservados, § Ritmos, § Rate limiting), `docs/02-esquema-bd.md`
> (`combat_log`, `characters.level/xp/hp/mp`).

---

## 1. Objetivo

Pegarse: contra monstruos en el campo, y contra otros jugadores **sólo donde la región lo
permite**. Con latencia real de por medio, sin que la latencia decida quién gana ni abra un
agujero de seguridad.

**Criterio de aceptación (docs/03):** dos clientes se pegan en el bosque y **no** pueden hacerlo
en la plaza, *ni siquiera atacando desde el borde*.

### Fuera de alcance, a propósito

- **Habilidades y curva de nivel: son la Fase 10.** El opcode `SkillCast` (`0x0061`) sigue
  reservado sin tipar, y `characters.level` no sube en esta fase. La XP sí se mueve (los
  monstruos la dan, morir en PvP la quita) porque `combat_log.xp_lost` y `XpUpdate` lo piden,
  pero *cuánta* XP hace falta para subir es la curva de la Fase 10.
- **Detectores de anomalías y métricas: son la Fase 13.** Aquí sólo va la validación propia del
  combate (alcance, cooldown, zona, línea de visión).

---

## 2. Las decisiones de diseño

### D1 — El servidor mide el RTT él mismo. `ProtocolVersion` 2 → 3

La compensación de latencia necesita saber cuánta latencia tiene cada jugador. Hoy **el servidor
no lo sabe**: `C2SPing` lleva un sello del reloj del cliente y `S2CPong` lo devuelve tal cual, así
que quien calcula el RTT es el cliente, para su HUD. Preguntárselo sería regalarle el control de
cuánto se rebobina el mundo a su favor — exactamente el tipo de cosa que CLAUDE.md §4 prohíbe
("el cliente nunca envía... sólo intenciones").

Para medirlo en servidor hace falta que un sello **originado en el servidor** vuelva. El único
mensaje C2S que puede llevarlo es `Ping`, así que `C2SPing` gana `LastServerTimeMs`: el eco del
último `S2CPong.ServerTimeMs` que vio el cliente. El servidor calcula
`rtt = ServerClock.NowMs - LastServerTimeMs` y lo suaviza.

Eso **cambia la forma de un mensaje que ya existía**, que es justo el criterio de `docs/01` para
subir de versión (a diferencia de los opcodes nuevos de las Fases 6–8, que no la subieron):
**`ProtocolVersion` pasa de 2 a 3.** Es el primer cambio de versión desde la Fase 4, y el motivo
es exactamente para el que existe el campo.

**Riesgo residual, aceptado y documentado:** un cliente parcheado puede devolver un
`LastServerTimeMs` viejo para inflar su RTT. No se le cree sin más: el rebobinado se **clampa a
200 ms** (§D2), que es el presupuesto que `docs/00 §6` da a un jugador honesto con mala conexión.
Es decir, mentir no le da a un tramposo nada que no tenga ya cualquiera con 400 ms de ping. Cerrar
esa ventana del todo exigiría desconfiar también del jugador honesto, y ése es el intercambio que
`docs/00 §6` ya eligió.

### D2 — La compensación mueve la **geometría**, nunca el **permiso**

Ésta es la decisión de seguridad de la fase, y no estaba escrita en ningún sitio.

`docs/00 §6` dice dos cosas que hay que combinar con cuidado:

1. el alcance se valida contra la posición que la víctima ocupaba en `now - RTT/2` (máx. 200 ms);
2. `CanAttack` exige que **ambos** estén en región `pvp`, "según las posiciones **autoritativas**".

Si se rebobinara también la comprobación de zona, aparecería un exploit nuevo justo al cerrar el
viejo: un atacante con 200 ms podría matar a alguien que **ya está dentro de la plaza**, porque
200 ms atrás todavía estaba fuera. Y al revés, alguien podría quedar protegido por haber estado en
zona segura hace un instante.

**Regla, por tanto:** el rebobinado se usa **sólo** para resolver el alcance (dónde estaba el
cuerpo). Los flags de zona —de atacante y de víctima— se resuelven **siempre contra la posición
autoritativa actual**. Llegar a zona segura protege en el acto; salir de ella expone en el acto.
Es la dirección de fallo segura: ante la duda, no se puede pegar.

### D3 — `CanAttack` falla cerrado

Ataque legal sólo si:

| Comprobación | Falla con |
|---|---|
| Objetivo existe y está vivo | `TargetNotFound` / `TargetDead` |
| No es uno mismo | `CannotAttackTarget` |
| Región del **atacante** tiene `Pvp` (sólo PvP) | `SafeZone` |
| Región de la **víctima** tiene `Pvp` (sólo PvP) | `TargetInSafeZone` |
| Cooldown de ataque cumplido | `OnCooldown` |
| Alcance (con rebobinado, D2) | `OutOfRange` |
| Línea de visión despejada | `OutOfRange` |

Una región sin ningún flag (`ZoneFlags.None`, un punto fuera de toda región declarada) **no** es
`pvp`: no se puede atacar ahí. Ya lo dice el comentario de `ZoneFlags`, y aquí es donde por fin
importa.

Contra monstruos no se exige `Pvp` en ninguna de las dos partes: pegarse con monstruos es legal
en cualquier región donde haya monstruos, y la plaza ya es `no_monsters`.

### D4 — RNG determinista con semilla de servidor, en `Shared`

`docs/03` pide "fórmulas de daño en `Shared` (deterministas y testeadas), RNG con semilla de
servidor". `DeterministicRng` (xorshift64*, 8 líneas, puro) vive en `Shared/Simulation` y se
puede probar con semilla fija: un test puede afirmar el daño **exacto**, no un rango.

La instancia real la crea el servidor al arrancar y no sale nunca de ahí; el cliente jamás recibe
la semilla ni la necesita (no predice daño: lo pinta cuando llega `CombatEvent`).

### D5 — Las fórmulas en `Shared`, la aplicación en `Server`

`CombatFormulas` es puro: entra atacante + defensor + una tirada, sale daño y si fue crítico.
Sin I/O, sin estado, sin acceso al mundo. Quien decide *si* se puede pegar y quien resta la vida
es `Server/Combat/CombatSystem` — mismo reparto que `MovementSystem` (Shared) frente a
`Zone.Simulate` (Server), y que `InventorySystem`/`ShopSystem`/`FarmSystem` en las fases 6–8.

Las fórmulas de esta fase son **provisionales a propósito** (las reajusta la Fase 10 con la curva
real): daño = `max(1, ataque - defensa/2)` con ±15 % de dispersión y crítico ×2 según destreza.

### D6 — Aggro: una tabla por monstruo, no un objetivo único

Un solo `TargetId` haría que dos jugadores pegando al mismo monstruo se lo robaran a cada golpe.
`AggroTable` guarda amenaza por entidad; el objetivo es el máximo. La amenaza sube con el daño
hecho, se borra al morir el jugador o al salir de rango, y la tabla entera se limpia cuando el
monstruo vuelve a su sitio (D7).

### D7 — Correa (*leash*): el monstruo vuelve a su punto de aparición

Sin correa, un jugador arrastra un monstruo hasta la plaza y lo suelta encima de otro. Si el
monstruo se aleja más de `leashTiles` de su spawn, entra en `Returning`: deja de perseguir,
ignora daño, limpia el aggro, se cura y vuelve. Es la defensa clásica contra ese griefing, y
encaja con que la plaza sea `no_monsters` sin necesidad de comprobarlo en cada paso.

### D8 — Máquina de estados de IA, con el orden del tick de `docs/00 §4`

`Idle → Patrol → Chase → Attack → Returning`, resuelta entera dentro del tick de la zona, sin
async y sin tocar Postgres, exactamente como el movimiento de los jugadores. Un monstruo no es más
que una `WorldEntity` con una FSM y una `AggroTable`: no toca `AoiSystem` ni `SnapshotBuilder`,
igual que los NPC de la Fase 7 (la generalidad que se diseñó en la Fase 4 vuelve a salir gratis).

### D9 — Saco de loot con derecho de saqueo, y un opcode nuevo (hueco real)

Al morir un monstruo cae un `LootBagEntity`. Durante `lootRightsSeconds` sólo puede abrirlo quien
más daño le hizo; después, cualquiera; al agotarse `lootDespawnSeconds`, desaparece.

**Hueco real del protocolo cerrado:** no hay ningún opcode para coger algo de un saco.
`ContainerId.LootBag = 7` está reservado desde la Fase 6 y `LootDrop` (`0x8062`) también, pero el
C2S no existe — `InvMove` no vale, porque opera entre contenedores **del propio personaje** y un
saco es una entidad del mundo compartida. Se añade **`LootTake = 0x0062`**, mismo criterio que
`ShopRepair` en la Fase 7. (Añadir un opcode no subiría la versión por sí solo; ya sube por D1.)

### D10 — Muerte de jugador: sin drop, con penalización de XP, y `combat_log` sólo en PvP

Tal cual `docs/00 §6.3`: no se dropea inventario (el full-loot ahuyenta a los nuevos), se pierde
un porcentaje de XP y se reaparece en el pueblo con la vida al mínimo de partida. Sólo las
muertes **PvP** se escriben en `combat_log` — `docs/02` es explícito en que las muertes contra
monstruos no se guardan ("demasiado volumen, poco valor").

### D11 — Flag de combate de 10 s: el logout deja de ser instantáneo

`docs/00 §6.2`: entrar en combate PvP marca a los dos durante 10 s, y mientras dure, desconectar
no saca al personaje del mundo — se queda 10 s más, vivo y atacable. Sin esto, "me van a matar"
se resuelve con Alt+F4.

Implementación: `PlayerLeaveCommand` de un jugador con el flag puesto **no** lo saca; marca
`PendingLeaveAtMs` y el barrido lo saca cuando expire (o antes, si muere). El estado se persiste
igual al desconectar la sesión de red, así que un cierre de proceso en medio no pierde nada.

### D12 — HP/MP/XP por fin se persisten (hueco real de las Fases 2–8)

`characters.hp/mp/level/xp` existen desde la Fase 2, se **leen** en `CharSelect`… y no se han
escrito nunca: `UpdatePositionAsync` sólo guarda mapa, posición, orientación y (desde la Fase 7)
oro. Hasta ahora daba igual porque nada cambiaba la vida; con combate, un jugador moribundo se
curaría del todo reconectando.

`PositionSave` pasa a llamarse **`CharacterSave`** y gana `Hp`, `Mp`, `Xp` y `Level`. En la Fase 7
decidí *no* renombrarlo para no tocar código ya probado; ahora que el struct es una instantánea
del personaje entero y no de su posición, el nombre viejo engaña más de lo que ahorra.

### D13 — Los monstruos no se persisten

Estado efímero: al arrancar, el mundo los crea desde `content/` en sus puntos de aparición. No
hay tabla de monstruos ni la va a haber; `world_state` (docs/02) queda para jefes con horario, que
no son de esta fase.

### D14 — Los puntos de aparición van en el mapa, y no cambian su hash

`docs/03` pide "puntos de spawn **en el mapa**". `MapDefinition` gana `spawns[]`. El hash de mapa
(`MapLoader.ComputeHash`) cubre geometría, regiones y punto de entrada — **no** los spawns, así
que añadirlos no invalida el contenido del cliente, que además no los necesita: los monstruos le
llegan por `EntitySpawn` como cualquier otra entidad.

---

## 3. Migración de BD

`db/migrations/0005_combat.sql`: `combat_log` tal cual `docs/02` (sólo PvP, D10).

## 4. `Shared`

| Fichero | Qué es |
|---|---|
| `Simulation/DeterministicRng.cs` | xorshift64*, puro, con semilla (D4). |
| `Simulation/CombatFormulas.cs` | Daño, crítico, alcance efectivo (D5). Puro y testeado. |
| `Simulation/LineOfSight.cs` | Trazado sobre `CollisionMap`: no se pega a través de un muro. |
| `Data/CombatConstants.cs` | Cooldown base, alcance base, historial de 500 ms, margen de 200 ms. |
| `Net/Messages/` | `C2SAttack`, `C2SLootTake`, `S2CCombatEvent`, `S2CEntityDeath`, `S2CLootDrop`, `S2CXpUpdate`, `S2CEntityStats`, `S2CCombatFlagUpdate`. |
| `Net/Opcode.cs` | `LootTake = 0x0062` (D9). |
| `Net/ProtocolVersion.cs` | 2 → 3 (D1). |
| `Net/Messages/C2SPing.cs` | gana `LastServerTimeMs` (D1). |

## 5. Servidor

| Fichero | Qué es |
|---|---|
| `Content/MonsterDefinition/Loader/Catalog.cs` | `content/monsters/*.json`, servidor-only (el cliente no necesita el catálogo, igual que los cultivos en la Fase 8). |
| `World/MonsterEntity.cs` | `WorldEntity` + FSM + `AggroTable` (D6, D8). |
| `World/LootBagEntity.cs` | `WorldEntity` con contenido y derechos (D9). |
| `Combat/CombatSystem.cs` | Validación y aplicación. Puro dado el mundo, sin I/O. |
| `Combat/PositionHistory.cs` | Anillo de 500 ms para el rebobinado (D1, D2). Server-only: el cliente no rebobina nada. |
| `Combat/MonsterAi.cs` | La FSM, aparte de la entidad para poder probarla sin mundo. |
| `Combat/MonsterSpawner.cs` | Puntos de aparición y respawn temporizado (D13). |
| `Persistence/Combat/` | `CombatLogRepository` + cola async, mismo patrón que economía/granja. |
| `Net/Session.cs` | Mide el RTT (D1) y lo expone por `IWorldPeer.RttMs`. |

## 6. Contenido

`content/monsters/{slime,wolf}.json`: vida, ataque, defensa, velocidad, alcance, cooldown, aggro,
correa, XP, tabla de loot. `content/maps/map.village.json` gana `spawns[]` en `campo_norte` (la
región `pvp`), nunca en `plaza` (`no_monsters`).

## 7. Cliente Godot

Sin arte. `NetClient` gana los eventos y `SendAttack`/`SendLootTake`; el HUD muestra vida propia y
del objetivo, y los números de daño. Igual que en las Fases 6–8, no se puede ejercitar a mano en
este servidor headless: sólo compila.

## 8. Tests

`Shared.Tests`: `DeterministicRngTests` (misma semilla → misma secuencia; distinta semilla →
distinta), `CombatFormulasTests` (daño exacto con tirada fija, mínimo 1, crítico),
`LineOfSightTests` (muro en medio, esquinas, mismo tile).

`Server.Tests`: `CombatSystemTests` (los siete rechazos de D3, incluido **atacar desde el borde de
la plaza**, que es el criterio de aceptación), `PositionHistoryTests` (rebobinado exacto, tope de
200 ms, historial agotado), `MonsterAiTests` (las cinco transiciones y la correa),
`AggroTableTests`, `CombatLogRepositoryTests` (`PostgresFact`).

## 9. Verificación sin Godot

`tools/Epimeteo.WorldBot --pvp`: dos bots de verdad, con `--lag-ms` real de por medio.

1. Los dos en `campo_norte` (`pvp`): A pega a B, B pierde vida → `CombatEvent`.
2. B camina a la plaza (`safe`); A vuelve a pegar → `TargetInSafeZone`, **la vida de B no baja**.
3. A entra en la plaza y pega a B, también dentro → `SafeZone`.
4. **El del criterio de aceptación:** A se queda en el borde exacto de la plaza y pega a B, que
   está fuera → rechazado igual (D3 exige `pvp` en el atacante).
5. Matar a un monstruo: cae el saco, sólo lo abre quien lo mató, aparece el ítem en su bolsa.
6. Matar a B en PvP: pierde XP, reaparece en el pueblo, hay fila en `combat_log`.
7. Flag de combate: B se desconecta en combate y su entidad **sigue** en el mundo (`--pvp` lo
   comprueba desde A, que sigue viéndola) hasta que expiran los 10 s.

## 10. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde.
2. Los 7 puntos de §9, contra el servidor de producción real.
3. HP/XP sobreviven a un reinicio del servicio (`psql`), como posición/inventario/oro/granja.
4. `combat_log` tiene fila por cada muerte PvP y **ninguna** por muerte de monstruo.
5. Cliente Godot compila sin warnings.

---

## 11. Resultado y hallazgos reales de la verificación E2E

Lo de arriba es el plan; esto es lo que pasó al ejecutarlo con
`tools/Epimeteo.WorldBot --pvp` contra el servidor de producción.

### Lo que encontró la verificación, y que no estaba previsto

- **El bot elegía cadáveres.** `KnownEntities` no se limpiaba con `EntityDespawn`, así que "pega
  al monstruo más cercano" acababa apuntando a uno que ya había matado el otro bot y el servidor
  respondía `TargetNotFound` cuarenta veces seguidas. Fallo de la herramienta, no del servidor,
  pero tapaba por completo la prueba de monstruos. Lo mismo con los sacos de loot: uno viejo
  seguía en la lista y era **de otro jugador**, así que el rechazo correcto por derechos de
  saqueo (D9) parecía un fallo del loot.
- **La penalización de XP se trunca a 0 con XP baja.** El 5 % de los 8 puntos que da un monstruo
  es 0,4, que en entero es 0. No es un fallo —protege justo a quien acaba de empezar— pero la
  expectativa del guion (`xp < xpAnterior`) sí lo era. Se comprobó lo que de verdad importa (que
  morir nunca sube la XP) y el descuento exacto se deja al test unitario, que puede fijar números.
- **Hay una banda del mapa que no pertenece a ninguna región.** `campo_norte` llega a `y = 48` y
  `pueblo` empieza en `y = 49`: la fila de la muralla queda sin región, con `ZoneFlags.None`. El
  guion comprobaba "está en zona segura" con `y > 48` y el margen de llegada del caminante (0,3
  tiles) dejaba al bot justo en esa banda. Se cambió a preguntarle al mapa por la región en vez de
  comparar coordenadas. **No es un fallo:** una región sin flags no es `pvp`, así que ahí no se
  puede atacar — es exactamente el fallo cerrado que quiere D3. Pero conviene saber que existe.

### Un límite honesto de la verificación E2E

El caso del criterio de aceptación —atacar **desde el borde**— se comprueba de dos formas y sólo
una es exacta:

- **`CombatSystemTests.DesdeElBordeDeLaPlaza_NoSePuedeAtacarAlDeFuera`** coloca a los dos a 0,2
  tiles de la frontera, **afirma primero que están en alcance** y luego que el golpe se rechaza
  por zona. Ése es el caso del criterio, con precisión de test unitario.
- El `--pvp` contra producción coloca a los bots a un lado y otro de la muralla real y comprueba
  que el servidor responde `combat.SafeZone`. Como `ValidateAttack` mira la zona **antes** que el
  alcance (D3), ese rechazo llega igual aunque el margen del caminante los deje algo más
  separados de 1,5 tiles. Prueba el camino entero de punta a punta, pero no fija la distancia.

Los dos juntos cubren el criterio; ninguno solo lo haría.

### Verificación real ejecutada

`tools/Epimeteo.WorldBot --pvp`, **24/24 comprobaciones en verde** en la corrida final:

1. ✅ Los dos bots cruzan a `campo_norte` y el servidor les dice que es zona hostil.
2. ✅ En zona PvP el golpe entra: baja la vida, llega `CombatEvent`, los dos quedan en combate.
3. ✅ A mata a B: B reaparece en el pueblo con vida, y morir nunca le sube la XP.
4. ✅ **Desde el borde de la plaza no se pega al de fuera**, con `combat.SafeZone` como motivo.
5. ✅ Con la víctima refugiada en la plaza, tampoco; ni con los dos dentro.
6. ✅ Monstruos: aparecen solos, se les pega, mueren, dan XP y sueltan botín que se puede coger.
7. ✅ **Desconectar en combate no saca del mundo**: A sigue viendo la entidad de B (D11).

En `psql`: `combat_log` tiene **exactamente una fila por muerte PvP y ninguna por las muchas
muertes de monstruo** (criterio 4), con región `campo_norte`. `characters.hp/xp` sobreviven un
`systemctl restart epimeteo` — el hueco de persistencia que abría D12 queda cerrado y comprobado.
Los monstruos vuelven a aparecer solos tras el reinicio, sin tabla que los guarde (D13). Tick medio
de **9 µs** con 6 monstruos vivos y 0 overruns.

357 tests en verde (137 `Shared` + 220 `Server`). Cliente Godot: compila sin warnings, con tecla
de ataque, objetivo más cercano y HUD de vida/XP/combate; como en las Fases 4–8, **no se ha
ejecutado** — sigue sin haber Godot en este servidor headless.
