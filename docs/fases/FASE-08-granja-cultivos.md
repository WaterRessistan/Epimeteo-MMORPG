# FASE 08 — Granja y cultivos

> Modelo: **Sonnet** (CLAUDE.md §6). Esquema de BD y opcodes ya cerrados desde las Fases 0–1
> (`farm_plots`/`farm_tiles` en `docs/02`, `FarmTill/Plant/Water/Harvest`/`FarmTileUpdate` en
> `docs/01`, `ResultCode` 500–504) — a diferencia de la Fase 7, **sin ningún hueco real** en el
> protocolo. Lo que sí hay que decidir de cero es cómo encaja el job diario (pensado en `docs/00 §7`
> como una sentencia SQL directa) en la arquitectura de tick-autoritativo que las Fases 4/6/7 ya
> fijaron.
>
> Prerrequisito leído: `docs/STATUS.md` (Fase 7 completa, en producción), `docs/00-arquitectura.md
> §7`, `docs/02-esquema-bd.md § Granja y cultivos`.

---

## 1. Objetivo

Arar, plantar, regar y cosechar una parcela compartida del pueblo. Los cultivos tardan ~3 días
**reales**; el progreso no se simula por tick, se cierra una vez al día a las 05:00 UTC. Regar
acelera (+1,0 de progreso) pero no regar nunca mata el cultivo (+0,5) — `docs/00 §7`.

**Criterio de aceptación:** plantar, adelantar el reloj del servidor 3 días (de verdad, no
simulado), cosechar.

---

## 2. Las decisiones de diseño

### D1 — El job diario se calcula en memoria, dentro del tick, no como un `UPDATE` SQL directo

`docs/00 §7` describe el job como una sentencia `UPDATE` masiva contra Postgres, pensada antes de
que las Fases 4/6/7 fijaran el patrón real del proyecto: **el tick es la única fuente de verdad,
Postgres es un espejo asíncrono de instantáneas** (mismo criterio que D1 de `FASE-07-tiendas.md`
con la "transacción SQL" de las tiendas).

Si el job corriera como SQL directo *además* de que las acciones del jugador (arar/plantar/regar/
cosechar) escriban en la misma tabla vía la cola asíncrona ya establecida, `farm_tiles` tendría
**dos escritores independientes de la misma fila** — el `UPDATE` masivo y la cola de guardado por
tile — con una condición de carrera real: cualquiera de los dos podría pisar al otro según quién
gane la carrera, sin que el segundo se entere. Ningún otro sistema del proyecto tiene ese
problema porque sólo hay un escritor por fila.

Por eso, igual que `ShopRuntime.SweepRestock` (FASE-07 §2 D8), el job diario vive dentro del tick:
una vez por segundo se comprueba si tocan uno o más días de granja (reloj de pared, `docs/00 §7`
punto 2), se aplica el crecimiento **en memoria** a cada tile plantado y se encola el guardado de
cada tile que cambió por la cola async de siempre. Un solo escritor, mismo patrón que todo lo
demás. El coste "una consulta para toda la granja" que `docs/02` prometía se cambia por "una fila
por tile que de verdad cambió, en la cola que ya existe" — con la única parcela de esta fase (48
tiles) es irrelevante; si la granja llega algún día a "miles de parcelas" (`docs/00 §7`), es del
mismo orden que arreglar la cola de posición o inventario para esa escala, no un problema de esta
fase.

**Recuperar días perdidos:** se guarda el último día de granja ya procesado (`farm_calendar`,
tabla nueva de una fila) y, al arrancar o en cualquier barrido, se procesan uno a uno todos los
límites de las 05:00 UTC que hayan pasado desde entonces — si el servidor estuvo caído 3 días,
se aplican 3 iteraciones seguidas del mismo cálculo, cada una con el instante de corte que le
tocaba, exactamente como pide `docs/03`.

### D2 — Una parcela comunitaria, sembrada por la migración; `farm_plots` se queda en Postgres

`farm_plots.owner_char_id` es nullable a propósito — `docs/02` ya distinguía "una parcela por
personaje (o comunitaria, con owner NULL)". Esta fase no implementa propiedad ni compra de
parcelas (no lo pide `docs/03`): una única parcela comunitaria en `map.village`, en la zona
abierta al sur del pueblo (fuera de los edificios de la Fase 7 y de la plaza), sembrada por la
propia migración con un `INSERT`. La geometría se queda en Postgres, no en `content/`, porque
`owner_char_id` ya anticipa que en el futuro esto es estado mutable (qué parcelas existen y de
quién) — no una decisión de diseño fija como el mapa o las tiendas.

### D3 — `farm_tiles` no se pre-rellena; se sintetiza en memoria y se fusiona con lo guardado

Igual que `ShopRuntime` (FASE-07 §5): al arrancar, el servidor construye en memoria un tile
"virgen" para cada `(x, y)` del rectángulo de la parcela y lo sustituye por lo que hubiera
guardado en `farm_tiles` si existe una fila. Un tile que nunca se ha arado no tiene fila en
Postgres hasta la primera vez que cambia — la migración no tiene que enumerar 48 filas.

### D4 — Herramientas: azada y regadera, un solo hueco de equipo, un campo nuevo para saber cuál es cuál

`EquipSlot.Tool`/`EquipCategory.Tool` ya estaban reservados desde la Fase 6 (comentario propio:
"herramientas de granja" como futura fuente de desgaste). Pero sólo hay **un** hueco de
herramienta: arar necesita azada y regar necesita regadera, y un jugador no puede llevar las dos
puestas a la vez (cambia de herramienta como en cualquier juego de granja). Sin nada que
distinga qué acción habilita cada herramienta, no hay forma de exigir "la herramienta correcta"
(lo que pide `docs/03`) más allá de "hay algo puesto".

Se añade `ItemDefinition.FarmToolAction` (`Till`/`Water`, nullable, sólo válido con
`equipCategory: Tool`) — content declara qué hace cada herramienta, el servidor no adivina nada.
`content/items/hoe.json` (azada, Till) y `watering_can.json` (regadera, Water); las dos de tipo
`Weapon` en el sentido amplio ya establecido en la Fase 6 ("arma" = equipable de combate/trabajo,
incluye escudos), van en la bolsa de armas. Ninguna de las dos se desgasta (`durabilityMax`
ausente): nada pide desgaste de herramientas en esta fase, sólo que estén puestas.

### D5 — Hueco real cerrado: `ResultCode.WrongTool = 505`

Los códigos 500–504 ya reservados (`TileOccupied`, `TileNotTilled`, `NotSeeded`,
`NotReadyToHarvest`, `WrongSeason`) no cubren "la herramienta equipada no es la que hace falta".
Mismo criterio que `ShopRepair` en la Fase 7: hueco real, se cierra con el siguiente valor libre
del bloque ya reservado, sin abrir ningún opcode nuevo.

### D6 — Validación de tile fuera de la parcela: `Kick`; y distancia real, como cualquier acción

Un cliente honesto sólo actúa sobre tiles que ha visto en un `FarmTileUpdate` — todos dentro de
la parcela. Pedir una acción sobre `(x, y)` que no pertenece a ninguna parcela conocida no es una
jugada legal que falla, es un dato imposible con el protocolo cerrado (mismo criterio que
`InputState` con dirección fuera de `[-1,1]`, Fase 4, o un `EquipSlot` no definido, Fase 6):
`KickSession(ProtocolError)`, resuelto en `GameWorld` antes de llegar a `FarmSystem` — que se
queda puro, sin conocer la geometría de ninguna parcela.

Aparte de eso, y por más que las nueve decisiones originales no lo mencionaran aparte: **las
cuatro acciones también validan distancia real** contra la posición autoritativa del jugador
(`IsWithinFarmRange`, 2 tiles — mismo mecanismo que `IsWithinShopRange` de la Fase 7), con
`ResultCode.TooFarAway` reutilizado tal cual. CLAUDE.md §4 es explícito y no negociable: "toda
petición se valida en servidor contra el estado del servidor: distancia...". Sin esto, un cliente
podría arar/plantar/regar/cosechar la parcela entera desde cualquier punto del mapa — un hueco de
seguridad real, encontrado al escribir la verificación E2E (§9) y corregido antes de darla por
buena, no algo que estuviera ya cubierto por D1–D5.

### D7 — Sin `FarmResult`: los fallos van por `SystemMessage`

`docs/01` reservó `FarmTileUpdate` (sólo servidor) pero ningún opcode de resultado para las
cuatro acciones de granja — a diferencia de las tiendas, que sí tenían `ShopResult` reservado
desde la Fase 1. Mismo criterio que el inventario en la Fase 6: `SystemMessage` con clave
`farm.{ResultCode}`.

### D8 — Estación: implementada de verdad, pero el cultivo de esta fase no la usa

`docs/03` pide "estación" en el contenido de cultivos y `WrongSeason` ya estaba reservado. Se
implementa una función determinista, sin calendario propio que persistir: la estación es una
función pura del día del año en UTC (`FarmCalendar.SeasonOf`), en cuatro tramos iguales. El único
cultivo de esta fase (`crop.wheat`) declara `season: "Any"` a propósito — así la verificación E2E
no depende de en qué mes real se ejecute. El camino de rechazo (`WrongSeason`) se prueba con un
cultivo sintético en un test puro con un instante fijo, no contra contenido real.

### D9 — Calidad: la racha de riego, tal cual, en el `Quality` que ya existe

`water_streak` → bonus de calidad, tal como dice `docs/02`. Se reutiliza
`ItemStackInfo.Quality`/`InventorySystem.TryAddNew(quality:)` de las Fases 6–7 sin tocarlos:
`quality = min(waterStreak, 3)`, tope arbitrario y simple, no hace falta configurar nada en
`content/`.

### D10 — Cosechar vuelve la tierra a "arada", no a "virgen"; sin multicosecha esta fase

`docs/03` no pide cultivos multicosecha ni fertilizante, y ninguno lo hace real todavía aunque
`docs/02` los columnas correspondientes (`fertilizer_key`, `harvests_left`) ya estén en el
esquema cerrado. Se dejan sin usar (siempre `NULL`), igual que `durabilityMax` se dejó sin usar
en ítems que no lo declaraban en la Fase 6 — no reabre el esquema, simplemente no todas las
columnas tienen lógica todavía. Cosechar limpia el cultivo y deja el tile en `Tilled` (1), no en
`Untilled` (0): la tierra no se desara sola.

### D11 — `FarmTileUpdate` se manda a toda la zona, no por AOI

La parcela es pequeña (48 tiles) y su tabla de tiles vive en `GameWorld`, no en `Zone`/`AoiGrid`
(no son `WorldEntity`, no hace falta que un jugador "las vea aparecer" al acercarse). Se manda a
todos los jugadores del mapa de la parcela, sin filtrar por celda — lo mismo que `docs/00 §7`
llama "miles de parcelas" es un problema de escala futura, no de esta fase.

### D12 — ETA optimista, recalculada al plantar y en cada barrido diario

`docs/02`: "estimación para la UI, recalculada en el job diario". Se calcula asumiendo riego
todos los días que falten (el mejor caso, +1,0/día): `eta = límite del día actual +
ceil(pendiente)` días. Se recalcula también al plantar (no sólo en el barrido) para que la UI no
se quede en blanco hasta el primer día — extensión menor, no cambia el contrato con `docs/02`.

---

## 3. Migración de BD

`db/migrations/0004_farm.sql`: `farm_plots` + `farm_tiles` tal cual `docs/02` (columnas
`fertilizer_key`/`harvests_left` incluidas aunque sin lógica, D10), `farm_calendar` (una fila,
`last_day_index`), `INSERT` de la parcela comunitaria de `map.village` y el `last_day_index`
inicial calculado con la misma fórmula que `FarmCalendar.DayIndex` en C# (frontera de referencia
`2000-01-01T05:00:00Z`) para no disparar una recuperación de "días perdidos" falsa en el primer
despliegue.

## 4. `Server/Content/` (nuevo, no `Shared`)

`CropDefinition`/`CropLoader`/`CropCatalog`: mismo estilo validador que `ItemLoader`, pero viven
en `Server/Content/` (como `ClassCatalog`/`MapCatalog`, no como `ItemCatalog`/`ShopCatalog`)
porque el cliente no necesita el catálogo — sin arte, pinta la clave y el `stage` que ya manda el
servidor, igual que `ItemSlot` pinta `DefKey` recortado sin catálogo (Fase 6).

`content/crops/wheat.json`: `growthDaysNeeded: 3`, `season: "Any"`, `stages` (3, cosméticos).

## 5. `Shared/Data/` (nuevo)

`FarmTileStatus` (0 virgen / 1 arado / 2 plantado / 3 listo, igual que `farm_tiles.state`),
`FarmToolAction` (`Till`/`Water`). `ItemDefinition` gana `FarmToolAction?` (D4).

## 6. Servidor

- `Server/Farm/FarmSystem.cs`: puro, como `ShopSystem`/`InventorySystem` — `TryTill`/`TryPlant`/
  `TryWater`/`TryHarvest` + `ApplyDailyGrowth` (la función que antes iba a ser el `UPDATE` SQL,
  D1), todas testeables sin BD ni tick.
- `Server/Farm/FarmCalendar.cs`: puro — `DayIndex`/`BoundaryOf`/`SeasonOf`/`EstimateEta`.
- `Server/Farm/FarmTileState.cs`, `FarmPlotRuntime.cs`, `FarmRuntime.cs`: mismo papel que
  `ShopStockState`/`ShopRuntime` (FASE-07 §5).
- `Persistence/Farm/`: `FarmPlotRepository`, `FarmTileRepository`, `FarmCalendarRepository`
  (Dapper), `FarmTileSaver` (`IFarmSink` + `IHostedService`, misma cola-descartar-lo-viejo que
  posición/inventario/economía).
- `GameWorld`: gana `_farmRuntime`/`_crops`/`_farmSink`; `SweepFarmGrowth(tick)` una vez por
  segundo (D1); handlers para los 4 opcodes de granja; `FarmTileUpdate` inicial al entrar al
  mundo (como `InventoryFull`).
- `content/shops/general_store.json` gana `hoe`/`watering_can` en su lista de venta — sin eso,
  nadie puede conseguir una herramienta (no hay otra fuente todavía).

## 7. Cliente Godot

Sin arte. `NetClient` gana `FarmTileUpdateReceived` y los 4 `Send*`. Fuera de alcance de esta
fase escribir la pantalla de granja en sí (tecla propia, rejilla de tiles clicable) — el patrón ya
está probado tres veces (inventario, tienda); si hay tiempo se añade al cierre, si no, queda
igual de pendiente que el resto de la UI sin ejecutar en este servidor headless.

## 8. Tests

`Epimeteo.Server.Tests`: `FarmSystemTests` (arar sin herramienta, arar con la herramienta
equivocada, plantar en tile no arado, plantar semilla equivocada, `WrongSeason` con cultivo
sintético, regar sin plantar, regar con herramienta equivocada, cosechar sin madurar, cosechar
sube calidad según racha, cosechar deja el tile arado). `FarmCalendarTests` (puro: `DayIndex`
monótono cada 24 h exactas, `SeasonOf` en los cuatro tramos, `EstimateEta`). `CropCatalogTests`
(contenido real). `FarmTileRepositoryTests`/`FarmCalendarRepositoryTests` (`PostgresFact`).

## 9. Verificación sin Godot

`tools/Epimeteo.WorldBot --farm-plant` + `--farm-harvest <username>`: comprar azada+regadera+
semilla en la tienda general, caminar a la parcela, arar, plantar trigo, regar una vez. Para los
días: en vez de esperar de verdad, se manipula `farm_calendar.last_day_index` por `psql`
restándole 6 (mismo criterio que la durabilidad manipulada a mano en la Fase 7 — y con margen de
sobra: sólo se riega una vez, así que el progreso sube 0,5/día a partir de ahí, no 1,0) y se
reinicia el servicio (el barrido compara contra lo que ya tiene en memoria, no relee Postgres
sin más) para que detecte los días perdidos y los procese de una vez — es la ruta de recuperación
real, no un atajo de test. Se comprueba `state=Ready` y se cosecha, verificando que el trigo
aparece en el inventario con la cantidad y calidad esperadas.

## 10. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde con los tests nuevos.
2. Arar sin azada, regar sin regadera → `WrongTool`, nada cambia.
3. Plantar, regar, recuperar varios días perdidos (`psql` + reinicio real), cosechar → ítem en
   el inventario con la cantidad y calidad esperadas.
4. `farm_tiles`/`farm_calendar` sobreviven un reinicio del servicio real (mismo criterio que
   posición/inventario/economía en las Fases 4/6/7).
5. Cliente Godot compila sin warnings.

---

## 11. Resultado y hallazgos reales de la verificación E2E

Lo de arriba es el plan; esto es lo que pasó al ejecutarlo de verdad contra producción con
`tools/Epimeteo.WorldBot --farm-plant` / `--farm-harvest <username>`.

**Un hueco real, no anticipado en las doce decisiones de diseño, encontrado al escribir el guion
de verificación — no por lectura de código:** ninguna de las cuatro acciones de granja
comprobaba la distancia entre el jugador y el tile. CLAUDE.md §4 es explícito y no negociable
("toda petición se valida en servidor contra... distancia") y las tiendas ya lo hacían desde la
Fase 7 (`IsWithinShopRange`) — un descuido real al portar el patrón a granja, no una decisión
consciente. Corregido con `IsWithinFarmRange` (2 tiles) y `ResultCode.TooFarAway` reutilizado tal
cual, en las cuatro acciones, antes de dar la fase por cerrada (ampliación de D6, §2).

**Segundo hallazgo, en la propia herramienta de verificación, no en el servidor:** el plan
asumía que "adelantar 3 días" bastaría para madurar el trigo (`growthDaysNeeded: 3`). Pero el
guion sólo riega **una vez** antes de simular el paso del tiempo — regar acelera pero no repite
solo (D1: regar acelera, sin regar suma 0,5, `docs/00 §7`) — así que el progreso real fue
`1,0 (el día regado) + 0,5 + 0,5 = 2,0`, no `3,0`: el tile se quedó en `Planted`, no en `Ready`.
La lógica del servidor estaba bien; la expectativa del test estaba mal. Corregido rodando 6 días
en vez de 3 (el mismo margen que el peor caso "abandonado" de `docs/00 §7`) — cubre de sobra
`1,0 + 0,5×5 = 3,5 ≥ 3`. De paso confirmó algo no obvio hasta verlo correr: **hace falta
reiniciar el servicio** para que el barrido note un `farm_calendar.last_day_index` movido a mano
por SQL — el barrido compara contra lo que ya tiene en memoria (`FarmRuntime.LastProcessedDayIndex`,
D1), no relee Postgres en cada pasada. En producción esto nunca hace falta (el índice sólo avanza,
nunca lo mueve nadie hacia atrás desde fuera); sólo importa para esta manipulación de prueba —y
es, además, el escenario más realista posible: "el servidor estuvo caído y al volver recupera los
días perdidos", exactamente lo que pedía `docs/03`.

**Verificación real ejecutada:** `--farm-plant` (comprar azada+regadera+semilla en el almacén
general, caminar ~45 tiles hasta la parcela, arar, plantar, regar — **8/8** comprobaciones)
seguido de `UPDATE farm_calendar SET last_day_index = last_day_index - 6` + `systemctl restart
epimeteo` + `--farm-harvest <username>` (el tile ya en `Ready` al reconectar, cosechar da trigo
de verdad, el tile vuelve a `Tilled` — **5/5** comprobaciones). **13/13 en verde** en la corrida
final, limpia, tras corregir los dos hallazgos de arriba. Verificado también por `psql`: `state`,
`crop_key` y `growth_days` del tile cosechado sobreviven un `systemctl restart epimeteo` sin
pérdida, mismo criterio que posición/inventario/economía en las Fases 4/6/7.

173 tests de servidor en verde (frente a los 133 previos a esta fase: +40 nuevos, incluidos los
de `FarmSystem`/`FarmCalendar`/`CropCatalog` puros y `FarmTileRepository`/`FarmCalendarRepository`
contra Postgres real) + 117 compartidos. Cliente Godot: `NetClient` gana `FarmTileUpdateReceived`
y los 4 `Send*`, sin pantalla de granja dedicada (fuera de alcance explícito, §7) —
`dotnet build client/Epimeteo.Client.csproj` en verde, 0 warnings; sin Godot instalado en esta
máquina no se pudo abrir el editor ni probar la UI a mano, mismo límite que las fases anteriores.
