# FASE 04 — Mundo y movimiento autoritativo

> Modelo: **Opus** (CLAUDE.md §6). Es la fase que decide si el proyecto es un MMO o una demo.
> Todo lo que venga después (combate, granja, tiendas) se apoya en las tres piezas que se cierran
> aquí: **entidades con id propio**, **el tick simulando de verdad** y **AOI**.
>
> Prerrequisito leído: `docs/STATUS.md` (Fase 3 cerrada), `docs/00 §3, §4, §6`, `docs/01`.

---

## 1. Objetivo

Que dos clientes se muevan por un mapa real, con colisiones, viéndose el uno al otro, con el
servidor como única autoridad sobre la posición, y que se juegue bien con 150 ms de latencia.

Al terminar la fase existe:

1. Un mapa de verdad en `content/maps/map.village.json` con colisión por tiles y regiones.
2. `Epimeteo.Shared/Simulation/MovementSystem` ejecutado **literalmente por los dos lados**.
3. El bucle de tick simulando entidades, con `InputState` entrando y `Snapshot` saliendo.
4. Predicción + reconciliación en el cliente (sin goma elástica).
5. Interpolación de entidades remotas con 100 ms de buffer.
6. AOI por celdas de 16×16 tiles con `EntitySpawn`/`EntityDespawn`.
7. Guardado de posición cada 30 s y al desconectar, siempre fuera del tick.

**Fuera de alcance** (§11): monstruos, combate, `Interact`, chat, inventario, portales entre mapas,
ciclo día/noche y arte. El PvP se **calcula y se comunica** (`ZoneFlagsUpdate`), pero no hay nada
que prohibir todavía porque no hay ataques.

---

## 2. Las cinco decisiones de diseño

Estas cinco cosas son la fase. El resto es mecanografía.

### D1 — El input es un comando de paso fijo, no un intervalo de tiempo

El cliente **no** integra por frame con su `dt`. Acumula tiempo real y, cada **50 ms exactos**,
produce un `InputState` y da **un paso de simulación** de 50 ms. El servidor, al consumir ese
input, da **exactamente el mismo paso de 50 ms**. Un input = un tick = un desplazamiento fijo.

Por qué, frente a lo que dice `docs/01 § Anti-cheat` ("`dtMs` se clampa a `[0,100]`"):

- Con `dt` variable, cliente y servidor integran números distintos y la reconciliación corrige
  *siempre*, aunque nadie haga trampas. Con paso fijo, la predicción es exacta salvo error de
  coma flotante.
- Clampar `dtMs` a 100 ms permite exactamente el doble de velocidad al que miente. Con paso fijo,
  el reloj del cliente **no entra en la simulación**: la única palanca que le queda es mandar más
  inputs, y eso lo tapa el presupuesto de inputs (D5).

`dtMs` sigue viajando en el mensaje (el protocolo está cerrado y el campo existe), pero el servidor
**no lo integra**: sólo lo registra para diagnóstico de jitter. Se anota en `docs/01`.

El render **no** se congela a 20 Hz: el jugador local se dibuja interpolando entre el paso predicho
anterior y el actual, con el resto del acumulador como alfa. Se mueve suave a 60+ fps con
simulación a 20 Hz.

### D2 — Determinismo por construcción, con la reconciliación como red de seguridad

`MovementSystem` sólo usa `+`, `-`, `*` y comparaciones sobre `float`. **Prohibido** dentro de
`Shared/Simulation`: `MathF.Sqrt`, trigonometría, `Vector2.Normalized()` de Godot, `double`,
`DateTime`, RNG, y cualquier tipo de Godot. La diagonal no se normaliza en tiempo de ejecución:
se multiplica por la constante `DiagonalFactor = 0.70710678f`.

Con eso, misma entrada + mismo estado ⇒ mismo resultado bit a bit en x86-64 y en arm64. Aun así,
**no dependemos de que sea perfecto**: la reconciliación tolera 0.05 tiles antes de corregir, así
que una diferencia de un ULP no produce ni un solo tirón. Un test compara el resultado de 10.000
pasos contra un hash fijado en el propio test para detectar si un refactor rompe el determinismo.

### D3 — Unidades: el tile es la unidad del mundo, y sólo el cliente sabe qué es un píxel

- Posición: `float` en **tiles**, origen en la esquina superior izquierda del mapa. `y` crece hacia
  abajo (igual que la pantalla y que el `TileMapLayer` de Godot: no hay conversión de signo).
- Velocidad de caminar: **4 tiles/s** = 0.2 tiles/tick = 64 px/s con tile de 16 px. Redondo a
  propósito.
- Hitbox del jugador: AABB de **0.75 × 0.5 tiles** anclado a los pies (el pivote de `docs/00 §
  Presentación`). El personaje mide 16×32 px pero **sólo colisionan los pies**, como en Zelda/Stardew.
- El servidor no sabe qué es un píxel (CLAUDE.md §5). El factor 16 sólo aparece en el cliente.

### D4 — El cliente carga el mismo `content/maps/*.json` que el servidor, y se comprueba

Predecir el movimiento exige que el cliente conozca la colisión. Se resuelve con **una sola fuente
de verdad**: `MapDefinition` + `MapLoader` viven en `Shared/Data` y los dos lados leen el mismo
fichero de `content/maps/`.

- El servidor lo localiza con el `ContentPaths` que ya existe (Fase 3).
- El proyecto Godot recibe una copia: un target de MSBuild en `Epimeteo.Client.csproj` copia
  `content/maps/*.json` a `client/content/maps/` (gitignored) antes de compilar; el cliente lo lee
  con `FileAccess` desde `res://content/maps/`. Empaquetar eso en un export es problema de la
  **Fase 5** (filtro `*.json` en el export de Godot); se anota allí.
- Red de seguridad: `S2CWorldEnter` gana un campo **`MapHash`** (FNV-1a de dimensiones + rejilla de
  colisión). Si el hash del cliente no coincide, el cliente muestra "contenido desactualizado" y no
  entra, en vez de desincronizarse en silencio y culpar al netcode. **Sube `ProtocolVersion` a 2.**

### D5 — La cola de inputs del servidor es un jitter buffer con presupuesto

Por jugador, una cola de inputs pendientes. Cada tick del mundo:

- Consume **1** input. Si la cola tiene más de 3 pendientes (el cliente venía retrasado y llegaron
  en ráfaga), consume hasta **2** para vaciar el atasco sin acelerar visiblemente.
- Si la cola está vacía (paquete perdido o cliente lento), **no repite el último input**: simula
  con dirección cero. Un jugador que suelta el teclado y pierde el siguiente paquete no debe seguir
  andando en el servidor.
- Si la cola pasa de **10** pendientes, se descartan los más antiguos: es un cliente inundando o
  una recuperación de un corte largo, y reproducir 3 s de inputs viejos es peor que un salto.
- `lastAckedInputSeq` = `seq` del último input **consumido** (no del último recibido).
- Presupuesto: máximo **26 inputs aceptados por segundo** por sesión (20 nominales + 30% de margen
  para ráfagas), por encima del rate limit de red de 40/s que ya existe. Pasarse cuenta strike de
  anticheat, no de red.
- `seq` debe ser estrictamente creciente. Repetido o hacia atrás → se descarta (protege del replay
  trivial de un paquete de movimiento).

Además, control de desplazamiento (`docs/01`): se acumula la distancia recorrida por segundo y, si
supera `4 tiles/s × 1.15`, se registra un strike de anticheat. Con paso fijo esto no debería saltar
nunca; salta si alguien parchea el cliente para pasar dos veces por el `MovementSystem`.

---

## 3. Contenido — `content/maps/map.village.json`

Formato pensado para editarse a mano y verse en un `git diff`:

```jsonc
{
  "key": "map.village",
  "displayName": "Pueblo de Epimeteo",
  "width": 96,
  "height": 96,
  "spawn": { "x": 48.5, "y": 60.5, "facing": 2 },
  // Una cadena por fila, un carácter por tile. '#' sólido, '.' libre.
  // Longitud de cada fila = width, número de filas = height.
  "collision": [
    "################################################################################################",
    "#..............................................................................................#",
    …
  ],
  "regions": [
    { "name": "plaza",        "rect": [24, 48, 48, 48], "flags": ["safe", "no_monsters"] },
    { "name": "campo_norte",  "rect": [0, 0, 96, 48],   "flags": ["pvp", "outdoor"] }
  ]
}
```

- `rect` = `[x, y, ancho, alto]` en tiles.
- Resolución de región: **gana la primera que contiene el punto**, en orden del array. Un punto que
  no cae en ninguna región tiene `flags = none` (ni seguro ni PvP: por defecto no se puede atacar).
- El mapa se valida al cargar (§8). Un mapa mal formado **impide arrancar el servidor**, igual que
  una migración fallida: mejor no arrancar que arrancar con el mundo roto.

El mapa de esta fase es una prueba de netcode, no de diseño de niveles: 96×96 con muro perimetral,
un edificio con esquinas para probar el deslizamiento, un pasillo de 1 tile para probar que no se
atraviesa, y las dos regiones de arriba. 96×96 = **6×6 celdas de AOI**, suficiente para que dos
jugadores en extremos opuestos **no** se vean y el `EntitySpawn` al acercarse sea observable.

---

## 4. Mensajes de red

Los opcodes ya están reservados desde la Fase 1. No se inventa ninguno.

### Nuevos tipos en `Shared/Net/Messages/`

| Opcode | Tipo | Campos |
|---|---|---|
| 0x0020 | `C2SInputState` | `Seq:uint`, `DirX:sbyte`, `DirY:sbyte`, `Facing:byte`, `Flags:byte`, `DtMs:ushort` |
| 0x8020 | `S2CEntitySpawn` | `Entities: EntitySpawnInfo[]` |
| 0x8021 | `S2CEntityDespawn` | `Entries: EntityDespawnEntry[]` |
| 0x8022 | `S2CSnapshot` | `ServerTick:long`, `LastAckedInputSeq:uint`, `Entities: EntityDelta[]` |
| 0x8024 | `S2CZoneFlagsUpdate` | `RegionName:string`, `Flags:uint` |

- `EntitySpawnInfo`: `Id:int`, `Type:byte`, `DefKey:string`, `X:float`, `Y:float`, `Facing:byte`,
  `PaletteIndex:byte`, `Name:string`, `Hp:int`, `HpMax:int`.
- `EntityDespawnEntry`: `Id:int`, `Reason:byte`.
- `EntityDelta`: `Id:int`, `X:float`, `Y:float`, `Vx:float`, `Vy:float`, `Facing:byte`,
  `AnimState:byte`, `Flags:byte`.

`DirX`/`DirY` sólo admiten `-1`, `0`, `1`; cualquier otro valor → `Kick(ProtocolError)`. No se
clampa en silencio: un cliente honesto no manda 7.

Sin cuantización de posiciones en esta fase. 20 entidades × ~24 B × 10 Hz ≈ 5 KB/s por jugador; el
sitio para apretar está identificado (int16 en 1/64 de tile) pero optimizar antes de medir es
gastar la fase en lo que no toca.

### Cambio a un mensaje existente

`S2CWorldEnter` gana `[Key(7)] MapHash:uint` y `MyEntityId` pasa a ser el **id de entidad** real, no
`CharacterId` (era provisional, `docs/STATUS.md` ya lo advertía). ⇒ **`ProtocolVersion.Current = 2`**.

### Enums nuevos en `Shared/Net/` (o `Shared/Simulation/`)

`EntityType { Player=0, Monster=1, Npc=2, LootBag=3 }` ·
`DespawnReason { OutOfRange=0, Death=1, Logout=2 }` ·
`AnimState { Idle=0, Walk=1 }` ·
`Facing { North=0, East=1, South=2, West=3 }` (coincide con el comentario de `characters.facing`) ·
`[Flags] ZoneFlags { None=0, Safe=1, Pvp=2, NoMonsters=4, Outdoor=8, Indoor=16 }`.

---

## 5. `Epimeteo.Shared/Simulation/` — el código que corre en los dos lados

| Fichero | Qué es |
|---|---|
| `Vec2.cs` | `readonly record struct` de dos `float`. Sin `Length()` (usaría `sqrt`). |
| `TilePos.cs` | `readonly record struct` de dos `int`. |
| `SimulationConstants.cs` | `TickRate=20`, `TickDtMs=50`, `TickDt=0.05f`, `WalkSpeedTilesPerSec=4f`, `DiagonalFactor=0.70710678f`, `PlayerHalfWidth=0.375f`, `PlayerHalfHeight=0.25f`, `AoiCellTiles=16`, `ReconcileToleranceTiles=0.05f`, `InterpolationDelayMs=100`. |
| `CollisionMap.cs` | `bool[]` plano + `Width`/`Height`. `IsSolid(int,int)`, `IsBlocked(Vec2 centro, float hw, float hh)`. Fuera del mapa = sólido. |
| `MoveInput.cs` | `Seq`, `DirX`, `DirY`, `Facing`. |
| `MoveState.cs` | `Pos:Vec2`, `Vel:Vec2`, `Facing`, `Anim`. |
| `MovementSystem.cs` | `static MoveState Step(in MoveState, in MoveInput, CollisionMap)`. Puro, sin estado, sin asignaciones. |
| `RegionSet.cs` | `Resolve(Vec2) → (string name, ZoneFlags flags)`. |
| `AoiGrid.cs` | tile → celda, celda → 3×3 vecinas. Puro, testeable sin servidor. |

Algoritmo de `Step`, **por ejes separados** (permite deslizar por las paredes en vez de engancharse
en cada esquina, que es lo que hace que un juego top-down se sienta bien):

```
1. dir = (dirX, dirY) tal cual (-1/0/1)
2. speed = WalkSpeed * (dirX != 0 && dirY != 0 ? DiagonalFactor : 1)
3. delta = dir * speed * TickDt
4. probar X: candidato = (pos.X + delta.X, pos.Y); si IsBlocked → conservar pos.X
5. probar Y: candidato = (pos.X', pos.Y + delta.Y); si IsBlocked → conservar pos.Y
6. facing: si hay input, se deriva de la dirección (prioridad vertical); si no, se conserva
7. anim = (dir != 0 && la posición cambió) ? Walk : Idle
8. vel = (posFinal - posInicial) / TickDt   // sólo informativa, para el cliente
```

`Shared/Data/MapDefinition.cs` + `MapLoader.cs`: POCO del JSON y carga con `System.Text.Json`,
validación incluida, `ComputeHash()` FNV-1a. Vive en `Shared` porque lo usan los dos lados.

---

## 6. Servidor — `Epimeteo.Server/World/`

| Fichero | Qué es |
|---|---|
| `WorldEntity.cs` | `Id`, `Type`, `MoveState`, `LastChangedTick`, `CellIndex`. Base de monstruos y NPCs (Fases 9+). |
| `PlayerEntity.cs` | + `SessionId`, `CharacterId`, `Name`, `PaletteIndex`, `Hp/HpMax`, cola de inputs, `LastAckedSeq`, `LastSeq`, `Known:HashSet<int>`, `CurrentRegion`, `LastSaveTick`, presupuestos de anticheat. |
| `EntityIdAllocator.cs` | `Interlocked.Increment` sobre un `int`. Espacio de ids propio, empieza en 1. |
| `Zone.cs` | Una instancia por mapa. Dueña de las entidades, la `CollisionMap`, el `RegionSet` y la `CellGrid`. `Tick(long)`. |
| `CellGrid.cs` | `HashSet<int>[]` de celdas de 16×16 tiles. `Move(entity, oldCell, newCell)`. |
| `AoiSystem.cs` | Al cambiar de celda: 3×3 nueva vs `Known` → `EntitySpawn` de lo que entra, `EntityDespawn(OutOfRange)` de lo que sale. |
| `SnapshotBuilder.cs` | Un `Snapshot` por jugador cada 2 ticks: entidades de su AOI con `LastChangedTick > viewer.LastSnapshotTick`, **más siempre la propia** (lleva el ack). |
| `World.cs` | Diccionario de zonas + drenaje del inbox + enrutado por sesión. Lo llama `GameLoop`. |
| `MapCatalog.cs` | Carga `content/maps/*.json` al arrancar, como `ClassCatalog` (Fase 3). |

### Cambios en piezas existentes

- **`IWorldInbox` / `WorldInbox`**: además de `Post(sessionId, opcode, payload)`, gana
  `PostControl(WorldCommand)` para *join* y *leave*. La cola de control se drena **antes** que la de
  opcodes en cada tick, para que un input nunca llegue antes que el join de su jugador.
- **`GameLoop.Tick`**: el bloque "sistemas de mundo: vacío en la Fase 1" pasa a `_world.Tick(tick)`,
  con el orden exacto de `docs/00 §4`: control → inputs → simulación → AOI → snapshots → guardados.
- **`SessionMessageHandler`**:
  - `CharSelect` reserva ya el id de entidad (`EntityIdAllocator`, thread-safe) para poder mandarlo
    en `WorldEnter`, guarda el `Character` cargado en la sesión y añade `MapHash`.
  - `WorldReady` pasa a `InWorld` **y** postea el comando *join* con ese `Character` (nada de tocar
    Postgres desde el tick).
  - `InputState` ya cae solo en el `IWorldInbox` por la ruta genérica que dejó la Fase 1: sólo hay
    que quitar el `Warning` de "sin sistema que lo atienda".
- **`Session`**: propiedades `EntityId` y `PendingCharacter`. `Send` ya es seguro desde el hilo del
  tick (escribe en un `Channel`), así que el tick envía directo sin capa nueva.
- **`SessionManager`**: `TryGet(int id, out Session)` y `Remove` postea *leave* al mundo si la sesión
  llegó a entrar. El *leave* lo procesa el tick: quita la entidad, manda `EntityDespawn(Logout)` a
  quien la veía y encola el guardado final.
- **Doble login del mismo personaje**: al procesar un *join* de un `CharacterId` que ya tiene
  entidad viva, se expulsa a la sesión antigua con `KickReason.LoggedInElsewhere` (el motivo ya
  existe) y entra la nueva. Sin esto, dos pestañas del mismo personaje se pisan la posición al
  guardar.
- **`/status`**: entidades vivas, jugadores en mundo, tamaño de la cola de guardado.

### Persistencia (fuera del tick, sin excepción)

`Persistence/CharacterPositionSaver.cs`: `IHostedService` con un `Channel<PositionSave>`. El tick
sólo hace `TryWrite` de un struct. El servicio drena y llama a
`CharacterRepository.UpdatePositionAsync(id, mapKey, x, y, facing)` (método nuevo, `UPDATE` de 4
columnas + `last_played_at`).

- Cada 30 s por jugador, **escalonado**: `(tick + entityId) % (30 * TickRate) == 0`. Si no, los 200
  jugadores escriben en el mismo tick.
- Al salir del mundo (logout, kick, apagado) — este sí, prioritario.
- Al apagar, `GameLoopService.StopAsync` vacía la cola antes de terminar. Sin esto, un
  `systemctl restart` pierde hasta 30 s de movimiento de todo el mundo.

Sin migración de BD: `characters` ya tiene `map_key`, `pos_x`, `pos_y`, `facing` desde la Fase 2.

---

## 7. Cliente Godot — `client/scenes/World.tscn`

Sustituye a `WorldPlaceholder`. Sin arte: los assets siguen en placeholder (CLAUDE.md §5), así que
se dibuja con `_Draw` — tiles sólidos en gris oscuro, entidades como rectángulos de 16×32 con el
color de su `PaletteIndex` y el nombre encima. Feo y suficiente para validar el netcode; cuando
lleguen los sprites, se cambia el renderer sin tocar la predicción.

| Fichero | Qué es |
|---|---|
| `scripts/World/WorldScreen.cs` | Orquesta: carga el mapa, compara `MapHash`, engancha los eventos de `NetClient`, mantiene el registro de entidades. |
| `scripts/World/LocalPlayer.cs` | Acumulador de 50 ms, lee input, llama a `MovementSystem.Step`, guarda en el buffer y manda `InputState`. |
| ~~`scripts/World/PredictionBuffer.cs`~~ | **No existe.** Al implementar se vio que era `Shared/Simulation/ClientPrediction.cs`, que ya lo hace y es lo que ejecuta el `WorldBot`. |
| ~~`scripts/World/Reconciler.cs`~~ | **No existe**, por lo mismo: está dentro de `ClientPrediction`. Tenerlo dos veces habría significado verificar una copia y jugar con otra. |
| `scripts/World/RemoteEntity.cs` | Identidad de la entidad. El buffer de muestras y la interpolación acabaron en `Shared/Simulation/EntityInterpolator.cs`, y el reloj de render en `InterpolationClock.cs`: era la única pieza de netcode que quedaba dentro del proyecto de Godot, es decir, la única imposible de probar en un servidor headless. Ahí tienen tests. |
| `scripts/World/WorldRenderer.cs` | `_Draw` de la rejilla y las entidades, con Y-sort por `pos.Y`. |
| `scripts/World/WorldCamera.cs` | Sigue al jugador local, se limita a los bordes del mapa, posición redondeada a píxel entero (si no, el pixel art tiembla). |
| `scripts/Ui/WorldHud.cs` | Posición, RTT, región actual + aviso "ZONA HOSTIL", y contador de correcciones/error máximo — el HUD **es** el instrumento de aceptación. |

Reloj de interpolación: el cliente mantiene `_renderTick` avanzando con el `delta` real; cada
`Snapshot` recibido fija el objetivo en `serverTick − 2` (2 snapshots = 100 ms). Si la diferencia
pasa de 5 ticks, salta; si no, corrige acelerando o frenando el reloj un 10 %. Saltar siempre se ve;
corregir el 10 % no se nota.

`NetClient` gana `SnapshotReceived`, `EntitySpawnReceived`, `EntityDespawnReceived`,
`ZoneFlagsUpdateReceived` y `SendInput(...)`; `WorldPlaceholder.tscn` y su script se borran.

**Simulador de latencia** (dev): `NetLagSimulator` dentro de `NetClient`, apagado por defecto, con
`EPIMETEO_LAG_MS` / `--lag-ms=150`. Retrasa envíos y recepciones con dos colas por tiempo. Es lo que
permite cumplir el criterio de aceptación sin depender de `tc netem` en la máquina de producción.

---

## 8. Validación y errores

| Situación | Respuesta |
|---|---|
| `DirX`/`DirY` fuera de `[-1,1]` | `Kick(ProtocolError)` |
| `Facing` fuera de `[0,3]` | `Kick(ProtocolError)` |
| `seq` repetido o menor que el último | Input descartado en silencio (replay/reordenación) |
| Cola de inputs > 10 | Se descartan los más antiguos, se registra |
| > 26 inputs/s aceptados | Strike de anticheat + log; 3 strikes en 10 s → `Kick(RateLimited)` |
| Desplazamiento > `4 × 1.15` tiles en 1 s | Se fuerza la posición autoritativa + strike |
| `InputState` de una sesión sin entidad | Se ignora (carrera con el *leave*, no es un ataque) |
| Mapa con filas de longitud ≠ `width` | El servidor **no arranca** |
| Región fuera de los límites del mapa | El servidor **no arranca** |
| `MapHash` distinto en el cliente | El cliente no entra y avisa: "contenido desactualizado" |

---

## 9. Tests

`Shared/Simulation` es obligatorio por CLAUDE.md §4. Objetivo: **~30 tests nuevos**.

**`Epimeteo.Shared.Tests`**
- `MovementSystemTests`: recto por los 4 ejes · la diagonal recorre lo mismo que el recto
  (`|Δ| = speed·dt`, no `√2` veces más) · deslizamiento contra pared en X y en Y · esquina cóncava
  (no atraviesa) · pasillo de 1 tile · sin input no se mueve · `facing` se conserva al soltar ·
  **no hay túnel**: 10.000 pasos contra un muro y la posición nunca queda dentro de un sólido.
- `MovementDeterminismTests`: 10.000 pasos con inputs pseudoaleatorios (semilla fija) → hash del
  estado final igual a la constante del test; y **replay**: simular N pasos de golpe ≡ simular hasta
  M y reejecutar los N−M restantes, bit a bit. Este segundo es *exactamente* la reconciliación.
- `CollisionMapTests`, `RegionSetTests` (dentro/fuera, solapamiento gana el primero, punto sin
  región = `None`), `AoiGridTests` (celda de un tile, vecindad 3×3, bordes y esquinas del mapa).
- `MapLoaderTests`: mapa válido · fila corta · carácter desconocido · región fuera de límites ·
  el hash cambia si cambia un tile y **no** cambia al reordenar claves del JSON.

**`Epimeteo.Server.Tests`** (sin Postgres salvo el repositorio)
- `InputQueueTests`: consumo de 1/tick, catch-up a 2, cola vacía → dirección cero, desbordamiento,
  `seq` no creciente, presupuesto de 26/s.
- `AoiSystemTests`: al cruzar de celda llegan `EntitySpawn` de los nuevos y `EntityDespawn` de los
  que salen, y **sólo una vez** (no re-spawn por tick).
- `SnapshotBuilderTests`: entidad quieta no se repite en snapshots consecutivos; la propia siempre
  va; `LastAckedInputSeq` es el último **consumido**.
- `CharacterPositionRepositoryTests` (`PostgresFact`): `UpdatePositionAsync` persiste y `GetOwned`
  lo devuelve.

---

## 10. Verificación sin Godot — `tools/Epimeteo.WorldBot`

Este servidor es headless (`docs/STATUS.md` § Entorno): el criterio "dos clientes moviéndose" no se
puede comprobar con el editor. Se comprueba con un tool nuevo que lanza **N clientes de verdad**,
hablando el protocolo real y ejecutando **el mismo `MovementSystem`, la misma predicción y la misma
reconciliación** que el cliente Godot (esa es la ventaja de que el netcode viva en `Shared`).

`--bots N` · `--lag-ms 150` · `--segundos S` · `--patron circulo|muro|encuentro`

Comprobaciones que emite (formato de `SmokeClient`, salida en verde/rojo y código de salida):

1. Los inputs se aceptan: `lastAckedInputSeq` avanza y no se queda atrás más de 3 seqs sin lag.
2. Snapshots a 10 ± 1 Hz.
3. **Sin goma elástica, 0 ms de lag**: error de reconciliación máximo < 0.05 tiles y **cero**
   correcciones en 30 s de carrera.
4. **Con 150 ms**: menos de 1 corrección por segundo y ninguna mayor de 0.3 tiles.
5. **Colisión**: el patrón `muro` empuja 20 s contra una pared; la posición autoritativa final nunca
   está dentro de un tile sólido y la desviación cliente-servidor no crece.
6. **AOI**: dos bots en extremos opuestos no reciben `EntitySpawn` el uno del otro; al acercarse a
   la misma celda 3×3, lo reciben exactamente una vez; al alejarse, `EntityDespawn(OutOfRange)`.
7. **Anti-speedhack**: un bot que manda inputs al triple de ritmo no recorre más distancia
   autoritativa que uno honesto (±5 %).
8. **Persistencia**: se mueve, se desconecta, vuelve a entrar y aparece donde lo dejó (± 0.2 tiles).
9. **Zonas**: al cruzar de `campo_norte` a `plaza` llega `ZoneFlagsUpdate` con `safe`.

El `SmokeClient` se amplía con lo mínimo (entrar al mundo y mandar un `InputState` legal e ilegal);
el resto vive en `WorldBot`, que es la herramienta de la fase y la base del load tester de la
Fase 14.

---

## 11. Fuera de alcance (para que no se me vaya la fase)

Monstruos, IA, aggro · combate y `Attack`/`Interact` · **aplicar** reglas PvP (sólo se calculan y se
comunican los flags) · inventario, tiendas, granja, chat · más de un mapa y portales · lag
compensation con historial de 500 ms (es la Fase 9, y sin combate no tiene nada que compensar) ·
ciclo día/noche · arte, sprites y animaciones · cuantización de snapshots · Redis y multiproceso.

---

## 12. Orden de trabajo

1. `Shared/Simulation` + `Shared/Data` (mapa, colisión, movimiento, regiones, AOI) **con sus tests**.
   Nada de red todavía: si esta capa no está verde, lo demás no vale nada.
2. `content/maps/map.village.json` + `MapCatalog` + validación al arrancar.
3. Mensajes nuevos + `ProtocolVersion = 2` + `MapHash` en `WorldEnter`.
4. `Zone`, entidades, cola de inputs, join/leave, tick simulando. Comprobable ya con logs.
5. `CellGrid` + `AoiSystem` + `SnapshotBuilder` → el servidor emite spawn/despawn/snapshot.
6. `CharacterPositionSaver` + vaciado al apagar.
7. `tools/Epimeteo.WorldBot` y las 9 comprobaciones. **Aquí se cierra el netcode**, antes de tocar
   Godot: depurar predicción con el editor abierto es diez veces más lento.
8. Cliente Godot: `World.tscn`, predicción, reconciliación, interpolación, cámara, HUD.
9. `docs/01-protocolo.md` (nota de `dtMs`, `MapHash`, versión 2), `docs/STATUS.md`, commit.

Si el punto 8 se complica (Godot no se puede probar en este servidor), se cierra la fase con el
netcode verificado por `WorldBot` y el cliente queda anotado como pendiente de una sesión con
entorno gráfico — igual que se hizo en las Fases 2 y 3, y por el mismo motivo.

---

## 13. Criterio de aceptación

1. `dotnet build` sin warnings y `dotnet test` en verde, con los ~30 tests nuevos.
2. `dotnet run --project tools/Epimeteo.WorldBot -- --bots 2 --segundos 30`: **9/9** comprobaciones
   en verde con 0 ms de lag.
3. Lo mismo con `--lag-ms 150`: 9/9 en verde (§10.4 es el criterio de "sigue siendo jugable").
4. `--bots 10`: el tick se mantiene por debajo de 5 ms de media y sin *overruns* en `/status`.
5. Un jugador se mueve, se desconecta y al volver a entrar sale donde lo dejó (comprobado además
   con `psql` sobre `characters.pos_x/pos_y`).
6. Reiniciar el servidor con jugadores dentro no pierde su posición.
7. Con Godot, si hay entorno gráfico: dos clientes en el mismo mapa, se ven moverse, el movimiento
   propio es inmediato y las paredes paran. Si no lo hay, se documenta como pendiente.

---

## 14. Riesgos

| Riesgo | Mitigación |
|---|---|
| La coma flotante no es idéntica entre x86-64 y arm64 | Sólo `+ - *` (D2), tolerancia de 0.05 tiles y test de determinismo con hash. Si aun así derivara, el plan B es punto fijo en enteros (mili-tiles) y el `MovementSystem` es el único fichero que cambia. |
| El cliente Godot no se puede probar aquí | El netcode se valida entero con `WorldBot`, que ejecuta el mismo código de `Shared` (§10). |
| La copia de `content/` al proyecto Godot se rompe en un export | `MapHash` lo convierte en un error ruidoso en vez de un desync silencioso; el empaquetado real es de la Fase 5. |
| La fase se hace enorme | El orden de §12 está pensado para poder parar en el punto 7 con algo íntegro y verificado. Si pasa, se anota en `STATUS.md` y se cierra la sesión (CLAUDE.md §6.4). |
