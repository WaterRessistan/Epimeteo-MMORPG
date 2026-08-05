# FASE 07 — Tiendas y armero

> Modelo: **Sonnet** (CLAUDE.md §6). Protocolo, esquema de BD y códigos de error mayormente ya
> cerrados desde las Fases 1–2 (`ShopOpen/Buy/Sell/Close`, `ShopData/ShopResult`,
> `CurrencyUpdate`, `shop_stock`/`economy_log`, `ResultCode` 400–404) — con **una laguna real**
> que esta fase tiene que cerrar (D6). El resto es implementación sobre lo cerrado.
>
> Prerrequisito leído: `docs/STATUS.md` (Fase 6 completa, en producción), `docs/01` (catálogo de
> mensajes, § Ritmos, § Rate limiting), `docs/02 § Economía y tiendas`.

---

## 1. Objetivo

Que un jugador se acerque a un tendero, abra su tienda, compre y venda con el oro real
descontándose/sumándose, y que el armero además repare una pieza de equipo desgastada. Todo
validado en servidor, con `economy_log` completo para poder investigar cualquier duplicación.

**Fuera de alcance:** comercio entre jugadores (`trade`, kind 9 de `economy_log`, reservado para
cuando exista), banco compartido (`container = 4`), buzón/correo, restock dinámico por
oferta-demanda (el restock es un temporizador fijo, no un algoritmo de mercado), y —importante—
**nada reduce durabilidad todavía**: no hay combate (Fase 9) ni herramientas de granja (Fase 8)
que la desgasten. El armero repara de verdad, pero hasta que exista una fuente de desgaste, sólo
se puede demostrar manipulando un ítem a mano (ver §9).

---

## 2. Las nueve decisiones de diseño

### D1 — La "transacción SQL" de `docs/02` se adapta al patrón ya construido

`docs/02` (escrito en la Fase 0, antes de que existiera el tick) dice: *"Toda compra/venta se
ejecuta en una transacción SQL: descontar oro, mover ítem, decrementar stock y escribir el log.
O todo, o nada."* Eso asumía resolver la compra fuera del tick, como `Login`/`CharCreate`. Pero
`ShopBuy`/`ShopSell` necesitan la posición y el inventario **autoritativos actuales** del
jugador, que sólo existen en memoria, dentro del tick (`PlayerEntity`) — cruzar de vuelta al
hilo de red rompería la regla de "un solo hilo toca al jugador" que sostiene toda la Fase 4.

Se resuelve igual que inventario (FASE-06 §2 D1/D2): la mutación es **en memoria, en el tick**
—oro, stock y bolsa cambian juntos, en el mismo hilo, sin intercalado posible con ningún otro
mensaje de ese jugador o de esa tienda: es la atomicidad real, no una ilusión de SQL— y la
persistencia a Postgres (oro, stock, log) es una instantánea asíncrona, exactamente como ya
hacen `CharacterPositionSaver` e `InventorySaver`. Perder una instantánea vieja de la cola nunca
importa: la siguiente ya contiene el resultado completo.

### D2 — El oro se persiste junto a la posición, no en un guardado nuevo

`PlayerEntity` no tenía oro hasta ahora (sólo viajaba una vez en `CharacterStats` al entrar).
Gana `Gold`. Para persistirlo, `PositionSave`/`CharacterPositionSaver`/`UpdatePositionAsync`
(Fase 4) ganan una columna más (`gold`) en el mismo `UPDATE` — es la misma fila de `characters`,
el mismo mecanismo de guardado por bandera sucia (`PositionDirty` gana un hermano `GoldDirty`, o
se funden en una sola bandera: cualquier cambio de estado persistible dispara el mismo guardado).
No se renombra la clase para no arrastrar un cambio de nombre por todo el código que ya la usa
desde la Fase 4 — el comentario de la clase se actualiza para decir lo que guarda de verdad.

### D3 — Los NPCs son `WorldEntity` estáticos, registrados al construir la zona

`WorldEntity`/`AoiSystem`/`SnapshotBuilder` ya están diseñados para esto desde la Fase 4 ("en la
Fase 4 sólo hay jugadores; monstruos, NPCs y sacos de loot heredarán de aquí sin tocar AOI ni
snapshots"). `NpcEntity : WorldEntity` no tiene cola de inputs ni inventario; su `MoveState`
nunca cambia tras crearse. Se registra en `_entities`/`_cells` al construir la `Zone` (no por
jugador, como los `PlayerEntity`) para que el `AoiSystem` de cualquier jugador cercano lo
descubra con el `EntitySpawn` que ya existe — cero código nuevo en AOI/snapshots.

### D4 — `content/shops/*.json`, uno por tienda, con su NPC dentro

Colocar el NPC en `content/maps/*.json` mezclaría geometría (lo que ya valida el `MapHash` para
la predicción) con contenido de entidades — no hace falta ni se toca. Cada tienda es un fichero
completo: definición + catálogo + dónde está su tendero. `MapHash` no cambia; los mapas no se
tocan.

```jsonc
{
  "key": "shop.armory",
  "displayName": "El Yunque de Hierro",
  "canRepair": true,
  "restockMinutes": 360,
  "npc": { "mapKey": "map.village", "x": 54.5, "y": 58.5, "facing": 2, "name": "Grommash", "paletteIndex": 2 },
  "items": [
    { "defKey": "item.iron_sword", "priceBuy": 80, "priceSell": 20, "stockMax": 5 },
    { "defKey": "item.wooden_shield", "priceBuy": 50, "priceSell": 12, "stockMax": 5 },
    { "defKey": "item.leather_chest", "priceBuy": 60, "priceSell": 15, "stockMax": 5 }
  ]
}
```

`stockMax` ausente o `null` = stock infinito (`docs/02`: *"si tienen stock infinito"*). El orden
del array de `items` **es** el `shopSlot` del protocolo (`docs/01`: `ShopBuy{shopSlot,...}`) —
estable mientras no se reordene el JSON a mano; reordenarlo es una decisión de contenido, no un
bug.

Dos tiendas en `map.village`, las dos en la plaza (zona segura): `shop.general_store`
(consumibles/materiales/semillas, no repara) y `shop.armory` (armas/armaduras, repara).

### D5 — Una tienda sólo recompra lo que ella misma vende

Vender un ítem que no está en el catálogo de esa tienda se rechaza (`ItemNotFound`, reutilizado:
"esta tienda no sabe qué es esto"). Sin esto, cualquier tienda se convertiría en un vertedero
universal de todo lo que un jugador no quiere — no es una regla que pida el roadmap
explícitamente, pero es la que evita el caso degenerado más obvio sin añadir un sistema nuevo.

### D6 — Reparar: opcode nuevo, hueco real en el protocolo cerrado

El catálogo de `docs/01` reserva `ShopOpen/Buy/Sell/Close` pero **no reserva nada para reparar**
— a diferencia de la Fase 6, donde todo lo necesario ya estaba reservado desde la Fase 1, aquí
falta. Se añade `ShopRepair = 0x0044` (siguiente hueco libre de la familia Shop, C2S). No hace
falta un mensaje de respuesta nuevo: el éxito se ve en un `InventoryDelta` (la durabilidad del
stack cambia, mismo mecanismo que cualquier otra mutación de inventario desde la Fase 6) +
`CurrencyUpdate` (el oro bajó); el fallo reutiliza `ShopResult`. Añadir un opcode nuevo **no**
sube `ProtocolVersion` — la regla de `docs/01` liga la versión a cambiar la forma de un mensaje
ya existente, no a añadir uno nuevo (mismo criterio que los 9 opcodes de inventario de la Fase 6,
que no la subieron).

Coste de reparar: `2 × (durabilityMax - durability)` de oro, redondeado hacia arriba. Fórmula
deliberadamente simple — no hay una economía de referencia todavía contra la que calibrar nada
más fino.

### D7 — La distancia se comprueba en cada acción, no sólo al abrir

Un jugador puede alejarse del NPC sin mandar `ShopClose`. `ShopOpen`, `ShopBuy`, `ShopSell` y
`ShopRepair` comprueban la distancia contra la posición autoritativa del NPC en ese instante
(`TooFarAway` si se pasa). Radio: 3 tiles, con margen sobre el tamaño de la caja del jugador.

### D8 — Restock: un temporizador por tienda, no por ítem

`restockMinutes` en el JSON de la tienda. Un barrido en el tick (mismo patrón que `SweepSaves`
de la Fase 4) repone **todo** el stock no infinito de una tienda a `stockMax` cuando toca,
de una vez. Un algoritmo de reposición gradual por ítem sería más "realista" y no lo pide nadie
todavía — es la clase de complejidad que CLAUDE.md pide evitar sin necesidad concreta.

### D9 — `economy_log`: se escribe en el mismo guardado asíncrono, y se cierra un hueco de la Fase 6

Cada compra/venta/reparación encola una fila de `economy_log` junto con el guardado de oro
(D2) — mismo criterio de instantánea-eventual que todo lo demás; el log no es la fuente de
verdad transaccional (eso es la memoria del tick, D1), es el archivo para investigar después.
**Se retoma `InvDrop` de la Fase 6**, que quedó sin loguear porque `economy_log` no existía
todavía: al crear la tabla en esta fase, `InvDrop` empieza a escribir `kind = 4` (tirar). No es
tocar lógica de la Fase 6, es completar lo que esa fase dejó explícitamente pendiente por falta
de tabla.

---

## 3. Migración de BD

`db/migrations/0003_shops_economy.sql`: `shop_stock` y `economy_log` tal cual las diseñó
`docs/02 § Economía y tiendas` (ya aprobadas, no se reabren). `characters.gold` ya existe desde
la Fase 2 — sin migración para eso.

---

## 4. `Shared/Data/`

| Fichero | Qué es |
|---|---|
| `ShopDefinition.cs`, `ShopItemDefinition.cs`, `ShopNpcPlacement.cs` | POCOs del JSON. |
| `ShopLoader.cs` + `ShopCatalog.cs` | Mismo patrón que `ItemLoader`/`ItemCatalog` (Fase 6): `Parse`/`Load` puros y testeables, `ShopCatalog` recorre el directorio. Vive en `Shared` porque el cliente necesita el catálogo para pintar `ShopData` con nombres, no sólo claves. |

`ItemDefinition` (Fase 6) gana `DurabilityMax` (nullable — sólo en los ítems que se desgastan;
por ahora `item.iron_sword` y `item.leather_chest`, para tener algo reparable en la armería).

---

## 5. Servidor

### `Server/Shop/` (nuevo)

| Fichero | Qué es |
|---|---|
| `ShopStockState.cs` | Stock actual + `RestockAtMs` de una tienda, en memoria. |
| `ShopRuntime.cs` | Todas las `ShopStockState`, cargadas al arrancar desde `shop_stock` (si no hay fila, `stockMax` del JSON) — vive en `GameWorld`, no por zona: una tienda es una entidad económica única aunque su NPC esté en un mapa concreto. |
| `ShopSystem.cs` | Estático y puro, mismo espíritu que `InventorySystem`: `TryBuy`, `TrySell`, `TryRepair`, dado `PlayerInventory` + `PlayerEntity` (oro) + `ShopStockState` + `ItemCatalog`. Devuelve `(bool Ok, ResultCode Code, ...)`, nunca lanza. |

### Cambios en piezas existentes

- **`PlayerEntity`**: gana `Gold`, `GoldDirty`, `OpenShopKey` (string?, qué tienda tiene abierta).
- **`NpcEntity`** (nuevo, en `World/`): `WorldEntity` sin más estado que su `ShopKey`.
- **`Zone`**: constructor gana la lista de NPCs de su mapa (de `ShopCatalog`, filtrados por
  `npc.mapKey`); los registra en `_entities`/`_cells` al construirse.
- **`PositionSave`/`IPositionSink`/`CharacterPositionSaver`/`CharacterRepository.
  UpdatePositionAsync`**: ganan `Gold` (D2).
- **`GameWorld`**: gana `ShopRuntime`, y en `DrainMessages`, los 5 opcodes de tienda
  (`ShopOpen/Buy/Sell/Close/Repair`), resueltos contra `ShopSystem` sin I/O. Barrido de restock
  en el tick (D8).
- **`Persistence/Economy/`** (nuevo): `EconomyLogRepository` (INSERT de una fila),
  `ShopStockRepository` (carga inicial + `UPSERT` de una tienda), ambos llamados desde el mismo
  guardado asíncrono que ya dispara D1/D2 — no un `IHostedService` nuevo, se añaden al mismo
  flujo de `CharacterPositionSaver` ampliado o a uno hermano si la mezcla de responsabilidades se
  vuelve confusa al escribirlo (se decide al implementar, no aquí).

### Validaciones (`ShopSystem`)

| Situación | `ResultCode` |
|---|---|
| Distancia al NPC > 3 tiles (D7) | `TooFarAway` |
| `ShopBuy`/`Sell`/`Repair` sin tienda abierta (o abierta otra) | `ShopNotOpen` |
| `shopSlot` fuera del catálogo de la tienda | `ItemNotFound` |
| Stock a 0 (no infinito) | `OutOfStock` |
| `precioEsperado` no coincide con el precio real (D5 del protocolo, ya cerrado) | `PriceChanged` |
| Oro insuficiente en compra/reparación | `NotEnoughGold` |
| Contenedor de destino lleno al comprar | `InventoryFull` |
| Vender un ítem que la tienda no compra (D5) | `ItemNotFound` |
| Vender más cantidad de la que hay | `NotEnoughItems` |
| Reparar un ítem con `DurabilityMax` nulo (no se desgasta) | `NotEquippable` (reutilizado: "esto no funciona así") |
| Reparar en una tienda con `canRepair = false` | `NotEquippable` |
| Reparar un ítem ya al máximo | ninguno — éxito sin cambios, mismo criterio que apilar ya al máximo en la Fase 6 |

---

## 6. Protocolo

Un solo opcode nuevo (`ShopRepair`, D6); todo lo demás ya estaba reservado. `C2SShopRepair {
container, slot }`. Tipar los `record` de MessagePack que faltan:

`C2SShopOpen { npcEntityId }` · `C2SShopBuy { shopSlot, quantity, expectedPrice }` ·
`C2SShopSell { container, slot, quantity, expectedPrice }` · `C2SShopClose {}` ·
`C2SShopRepair { container, slot }` ·
`ShopSlotInfo { defKey, priceBuy, priceSell, stock }` (stock `-1` = infinito) ·
`S2CShopData { shopKey, displayName, slots: ShopSlotInfo[] }` ·
`S2CShopResult { ok, code }` ·
`S2CCurrencyUpdate { gold }` (primera vez que se tipa; reservado desde la Fase 1).

---

## 7. Cliente Godot

Sin arte. Mismo criterio que el inventario de la Fase 6: la tubería de red importa, no la
estética.

| Fichero | Qué es |
|---|---|
| `client/scripts/Shop/ShopScreen.cs` | Overlay que aparece al recibir `ShopData` y se cierra con `interact` o su botón. Pestaña "Comprar" (una fila por `ShopSlotInfo`, botón que manda `ShopBuy`); pestañas "Vender" y "Reparar", cada una con las tres bolsas pintadas con `ItemSlot` de la Fase 6 (clic = vender/reparar ese hueco). Sin `ShopState.cs` aparte: reutiliza `Inventory.InventoryState` tal cual para el espejo de bolsas — es exactamente lo mismo que ya hace esa clase, así que una segunda casi idéntica sólo habría sido duplicación. |
| `client/scripts/World/WorldScreen.cs` | Gana: tecla de interacción (`E`, nueva acción `interact`) que manda `ShopOpen` al NPC (`EntityType.Npc`) más cercano dentro de rango si hay uno, o cierra la tienda si ya estaba abierta. El rango del lado cliente (3.5 tiles) es sólo para elegir a qué NPC apuntar — algo generoso a propósito, porque quien de verdad decide es el servidor (D7); ser generoso aquí sólo produce algún `TooFarAway` de más, nunca un hueco de seguridad. |

`NetClient` gana `ShopDataReceived`, `ShopResultReceived`, `CurrencyUpdateReceived`, `Gold`
(inicializado con `WorldEnter.Stats.Gold`, al día con cada `CurrencyUpdate`), y
`SendShopOpen/Buy/Sell/Close/Repair(...)`.

**Hueco real encontrado al construir esto, no anticipado en las nueve decisiones:** el protocolo
no manda una `ShopData` fresca tras una compra/venta con éxito (sólo `CurrencyUpdate` +
`InventoryDelta`, `S2CShopResult` es sólo de fallo), así que el precio/stock que pinta la pestaña
"Comprar" se quedaría desactualizado tras la primera compra. En vez de añadir un mensaje nuevo al
protocolo por esto, `ShopScreen` vuelve a pedir `ShopOpen` al mismo NPC cada vez que llega un
`CurrencyUpdate` mientras la tienda sigue abierta — barato (un `ShopOpen` de más, que además
revalida la distancia gratis) y no toca el protocolo cerrado.

---

## 8. Tests

**`Epimeteo.Shared.Tests`**: `ShopLoaderTests` (válida, `stockMax` ausente = infinito, `items`
vacío, categoría de repetición de `defKey` dentro de una tienda → error).

**`Epimeteo.Server.Tests`**: `ShopSystemTests` (el grueso, puro, sin BD/tick): comprar con precio
esperado correcto/incorrecto, comprar sin oro, comprar con la bolsa llena, comprar agotando el
stock exacto, vender sube el oro y baja el stock de bolsa, vender algo que la tienda no compra,
reparar restaura `durabilityMax` y cobra lo justo, reparar un ítem sin durabilidad →
`NotEquippable`, reparar en tienda sin `canRepair` → `NotEquippable`, distancia > 3 tiles →
`TooFarAway` en las cuatro acciones. `ShopCatalogTests`/`NpcPlacementTests` (contenido real,
como `ItemCatalogTests`). `EconomyLogRepositoryTests`/`ShopStockRepositoryTests` (`PostgresFact`).

---

## 9. Verificación sin Godot

Ampliar `tools/Epimeteo.SmokeClient` con el flujo de tienda tras `WorldReady`: acercarse al NPC
de la armería (falla por distancia primero, para probar `TooFarAway`), `ShopOpen` →
`ShopData`, comprar una espada de más (con oro insuficiente si el kit inicial no alcanza — si
alcanza, se vende algo primero para dejarlo justo), vender el escudo del kit inicial, y para
reparar: como nada desgasta ítems todavía (§1), el test baja la durabilidad de un ítem
**directamente por SQL** entre dos pasos del flujo (mismo criterio ya usado en la Fase 5/6 para
verificar backups y reinicios: acceso directo a Postgres cuando hace falta un estado que el
protocolo no puede producir todavía) y comprueba que `ShopRepair` la sube y cobra lo esperado.

---

## 10. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde con los tests nuevos.
2. Dos NPCs visibles (`EntitySpawn`) al entrar en la plaza del pueblo.
3. Comprar y vender cambian oro e inventario de punta a punta contra el servidor real, con
   `precioEsperado` rechazando un precio equivocado sin cambiar nada.
4. Reparar restaura la durabilidad y cobra el oro esperado (verificado con el ítem manipulado
   por SQL, §9).
5. `economy_log` tiene una fila por cada compra/venta/reparación, y por cada `InvDrop`.
6. Reiniciar el servidor tras una compra no pierde el oro ni el stock (comprobado en `psql`,
   mismo criterio que posición e inventario en las Fases 4 y 6).
7. El cliente Godot compila sin warnings; UI no verificable en este servidor headless (mismo
   criterio que las Fases 4 y 6).

---

## 11. Resultado y hallazgos reales de la verificación E2E

Lo de arriba es el plan; esto es lo que pasó al ejecutarlo de verdad contra producción con
`tools/Epimeteo.WorldBot` (extendido con movimiento hacia un punto — `SmokeClient` no anda, y
aquí hacía falta acercarse de verdad a un NPC para probar D7).

**Dos huecos reales, ninguno anticipado en las nueve decisiones de diseño, encontrados por la
propia verificación E2E — no por lectura de código:**

- **Sin oro inicial.** Ningún personaje nuevo podía comprar nada: `characters.gold` se quedaba
  en su `DEFAULT 0` de BD porque nunca se había necesitado hasta ahora. Se añadió
  `ClassDefinition.StartingGold` (mismo patrón que `StartingItems`, FASE-06 §2 D6) con
  `startingGold: 100` en las tres clases, insertado por `CharacterRepository.CreateAsync` en la
  misma fila que crea el personaje.
- **El oro guardado no viajaba al entrar al mundo.** Bug real, no de la Fase 7 en sí sino
  destapado por ella: `WorldJoinRequest` nunca llevaba `Gold`, así que `PlayerEntity.Gold` se
  quedaba en su valor por defecto (0) en cada join/reconexión, **aunque el personaje tuviera oro
  guardado de verdad en Postgres** — y el siguiente barrido de guardado lo habría sobrescrito con
  0. Corregido añadiendo `Gold` a `WorldJoinRequest`, pasándolo desde
  `SessionMessageHandler.character.Gold` y asignándolo en `Zone.Join`. Cubierto con una
  regresión nueva, `WorldTests.UnJoin_ConservaElOroGuardado`, que ningún test anterior habría
  detectado (nada probaba el oro a nivel de `GameWorld`, sólo a nivel de `ShopSystem` puro).

Un tercer hallazgo, menor: `EconomyLogKind` (fijado en `docs/02`, valores 1–9) no contemplaba
reparar porque es de antes de que existieran las tiendas — igual que el hueco de `ShopRepair` en
el protocolo (D6). Reparar se registraba como `Admin` (7), semánticamente incorrecto. Como la
columna es un `smallint` sin `CHECK`, se añadió `Repair = 10` sin migración.

**Verificación real ejecutada** (no sólo planeada): dos corridas de `WorldBot`
(`--shops-buy` / `--shops-repair <username>`, con una `UPDATE` manual por `psql` entre medias
para bajar la durabilidad de un arma — nada la desgasta todavía de verdad) contra el servidor de
producción tras cada despliegue con `deploy/publish.sh`. Las 14 comprobaciones (9 de compra/venta
+ 5 de reparación) pasaron en verde en la corrida final, con los tres hallazgos ya corregidos.
Verificado también por `psql`: `gold`, `durability` de los ítems, `shop_stock` y `economy_log`
(con `kind = 10` para la reparación) sobreviven un `systemctl restart epimeteo` sin pérdida,
mismo criterio que posición e inventario en las Fases 4 y 6.

245 tests en verde (117 `Shared` + 128 `Server`; +17 sobre los ~228 previstos en el plan, por las
tres correcciones de arriba). Cliente Godot: `dotnet build client/Epimeteo.Client.csproj` en
verde, 0 warnings; sin Godot instalado en esta máquina no se pudo abrir el editor ni probar la
UI a mano — la tubería (NetClient, eventos, `Send*`) sigue el mismo patrón ya probado del
inventario de la Fase 6, pero el flujo de clics en `ShopScreen` no se ha ejercitado de verdad.
