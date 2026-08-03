# 01 — Protocolo de red

## Formato de trama

Transporte: WebSocket **binario** (`wss://`). El framing de longitud lo aporta WebSocket, así que
cada mensaje es un frame:

```
[ uint16 opcode (little-endian) ][ payload MessagePack ]
```

- Un frame = un mensaje. Nada de batching manual en la fase inicial (nginx y WS ya agrupan).
- Payload serializado con **MessagePack-CSharp**, `[MessagePackObject]` con **claves numéricas
  explícitas** (`[Key(0)]`, `[Key(1)]`...). Nunca `keyAsPropertyName`: renombrar una propiedad
  no debe romper el protocolo.
- Límite duro: **16 KB** por frame entrante. Más grande → desconexión.
- Los opcodes viven en `Mmorpg.Shared/Net/Opcode.cs` como `enum Opcode : ushort`.
  Rango `0x0000–0x7FFF` = cliente→servidor, `0x8000–0xFFFF` = servidor→cliente.
  **Los valores no se reutilizan jamás.** Un opcode retirado se marca `[Obsolete]` y su número muere.

## Versionado

El primer mensaje de toda conexión es `C2S_Hello { protocolVersion, clientBuild }`.
Si `protocolVersion` no coincide con la del servidor → `S2C_Kick { reason = VersionMismatch }`
con la versión esperada, y el cliente muestra "actualiza el juego". `protocolVersion` es un
`int` que se incrementa a mano cada vez que cambia la forma de un mensaje existente.

## Máquina de estados de sesión

```
Connecting ──Hello──▶ Greeted ──Login/Register──▶ Authenticated
   ──CharSelect──▶ Loading ──cliente listo──▶ InWorld
```

Cada opcode declara sus estados legales en una tabla estática. Mensaje en estado ilegal →
log + cierre inmediato. No hay excepciones a esta regla.

## Catálogo de mensajes

### Cliente → Servidor (`0x0xxx`)

| Op | Nombre | Estados | Payload |
|---|---|---|---|
| 0x0001 | `Hello` | Connecting | protocolVersion, clientBuild |
| 0x0002 | `Login` | Greeted | usuario, password |
| 0x0003 | `Register` | Greeted | usuario, email, password |
| 0x0004 | `Ping` | * | clientTimeMs |
| 0x0010 | `CharListRequest` | Authenticated | — |
| 0x0011 | `CharCreate` | Authenticated | nombre, classKey, slot, aspecto |
| 0x0012 | `CharDelete` | Authenticated | characterId, confirmación |
| 0x0013 | `CharSelect` | Authenticated | characterId |
| 0x0014 | `WorldReady` | Loading | — (el cliente terminó de cargar el mapa) |
| 0x0020 | `InputState` | InWorld | seq, dirX, dirY, facing, flags, dtMs |
| 0x0021 | `Interact` | InWorld | targetEntityId \| tileX,tileY |
| 0x0030 | `InvMove` | InWorld | fromContainer, fromSlot, toContainer, toSlot, cantidad |
| 0x0031 | `InvUse` | InWorld | container, slot |
| 0x0032 | `InvDrop` | InWorld | container, slot, cantidad |
| 0x0033 | `Equip` | InWorld | container, slot, equipSlot |
| 0x0034 | `Unequip` | InWorld | equipSlot |
| 0x0040 | `ShopOpen` | InWorld | npcEntityId |
| 0x0041 | `ShopBuy` | InWorld | shopSlot, cantidad, precioEsperado |
| 0x0042 | `ShopSell` | InWorld | container, slot, cantidad, precioEsperado |
| 0x0043 | `ShopClose` | InWorld | — |
| 0x0050 | `FarmPlant` | InWorld | tileX, tileY, container, slot (semilla) |
| 0x0051 | `FarmWater` | InWorld | tileX, tileY |
| 0x0052 | `FarmHarvest` | InWorld | tileX, tileY |
| 0x0053 | `FarmTill` | InWorld | tileX, tileY |
| 0x0060 | `Attack` | InWorld | targetEntityId \| dirección, skillKey |
| 0x0061 | `SkillCast` | InWorld | skillKey, targetEntityId \| tileX,tileY |
| 0x0070 | `ChatSend` | InWorld | canal, texto |

> `precioEsperado` en compra/venta no es opcional: si el precio del servidor no coincide, la
> transacción se rechaza. Evita que un cambio de stock o una restock a mitad de clic cobre de más.

### Servidor → Cliente (`0x8xxx`)

| Op | Nombre | Payload |
|---|---|---|
| 0x8001 | `HelloAck` | serverProtocolVersion, tickRate, snapshotRate, serverTimeMs |
| 0x8002 | `AuthResult` | ok, código de error, accountId, sessionToken |
| 0x8004 | `Pong` | clientTimeMs (eco), serverTimeMs |
| 0x8005 | `Kick` | razón, mensaje |
| 0x8010 | `CharList` | lista de resúmenes (id, slot, nombre, clase, nivel, mapa, aspecto, equipo visible) |
| 0x8011 | `CharCreateResult` | ok, código de error, resumen |
| 0x8012 | `CharDeleteResult` | ok, código |
| 0x8013 | `WorldEnter` | mapKey, spawnX, spawnY, myEntityId, stats completos, hora del mundo |
| 0x8020 | `EntitySpawn` | lista de entidades que entran en AOI (id, tipo, defKey, pos, aspecto, nombre, hp/hpMax) |
| 0x8021 | `EntityDespawn` | lista de ids + motivo (fuera de AOI / muerte / logout) |
| 0x8022 | `Snapshot` | serverTick, lastAckedInputSeq, array de deltas (id, x, y, vx, vy, facing, animState, flags) |
| 0x8023 | `EntityStats` | id, hp, hpMax, mp, mpMax, nivel, buffs |
| 0x8024 | `ZoneFlagsUpdate` | nombre de región, flags (`safe`/`pvp`/...), enviado al cruzar de región |
| 0x8025 | `CombatFlagUpdate` | enCombate, msRestantes (bloquea logout limpio y teleport tras PvP) |
| 0x8030 | `InventoryFull` | contenedor completo (sólo al entrar al mundo o al abrir por primera vez) |
| 0x8031 | `InventoryDelta` | lista de slots cambiados (container, slot, itemInstance \| null) |
| 0x8032 | `EquipmentUpdate` | equipSlot → itemInstance \| null, + stats derivados recalculados |
| 0x8033 | `CurrencyUpdate` | oro (valor absoluto, nunca delta) |
| 0x8040 | `ShopData` | shopKey, nombre, lista de slots (defKey, precioCompra, precioVenta, stock) |
| 0x8041 | `ShopResult` | ok, código de error |
| 0x8050 | `FarmTileUpdate` | lista de tiles (x, y, estado, cropKey, etapa, regado, msRestantes) |
| 0x8060 | `CombatEvent` | atacanteId, objetivoId, tipo, cantidad, flags (crítico/esquiva/bloqueo), skillKey |
| 0x8061 | `EntityDeath` | id, killerId |
| 0x8062 | `LootDrop` | entityId del saco, pos, contenido visible |
| 0x8063 | `XpUpdate` | xpActual, xpSiguienteNivel, nivel, subióDeNivel |
| 0x8070 | `ChatMessage` | canal, remitenteNombre, texto, serverTimeMs |
| 0x8071 | `SystemMessage` | severidad, clave de i18n, argumentos |

## Códigos de error

Enum compartido `ResultCode : ushort`. El servidor **nunca** manda texto de error al cliente:
manda un código y el cliente decide cómo mostrarlo (permite traducir y no filtra internals).

```
Ok, UnknownError, RateLimited, InvalidState, VersionMismatch,
InvalidCredentials, AccountBanned, AccountAlreadyExists, NameTaken, NameInvalid,
SlotOccupied, NoCharacterSlots, CharacterNotFound,
InventoryFull, ItemNotFound, NotEnoughItems, NotEquippable, WrongClass, LevelTooLow,
NotEnoughGold, OutOfStock, PriceChanged, TooFarAway, ShopNotOpen,
TileOccupied, TileNotTilled, NotSeeded, NotReadyToHarvest, WrongSeason,
TargetNotFound, TargetDead, OnCooldown, NotEnoughMana, OutOfRange, CannotAttackTarget,
SafeZone, TargetInSafeZone, InCombat, LevelDifferenceTooHigh
```

## Ritmos

| Qué | Frecuencia |
|---|---|
| Tick de simulación | 20 Hz (50 ms) |
| `InputState` cliente→servidor | 20 Hz |
| `Snapshot` servidor→cliente | 10 Hz |
| Eventos discretos (combate, chat, inventario, tienda) | inmediato |
| `Ping` | 1 Hz |
| Timeout de sesión sin tráfico | 30 s |
| Buffer de interpolación del cliente | 100 ms |

## Rate limiting

Token bucket **por sesión y por familia de opcode**:

| Familia | Límite |
|---|---|
| `InputState` | 40 msg/s (2× el ritmo nominal) |
| Acciones de juego (inv, granja, combate) | 20 msg/s, ráfaga 40 |
| Tienda | 10 msg/s |
| Chat | 2 msg/s, ráfaga 5 |
| Login/Register | 5 por minuto **por IP**, no por sesión |

Superar el límite → `SystemMessage(RateLimited)`. Superarlo 3 veces en 10 s → desconexión.

## Anti-cheat mínimo desde el día 1

- `dtMs` del input se **clampa** en servidor a `[0, 100]`. El servidor no confía en el reloj del
  cliente; sólo lo usa para suavizar, y acumula un presupuesto de movimiento por segundo.
- Presupuesto de distancia: si la suma de desplazamiento en 1 s supera `velocidadMax * 1.15`,
  se corrige la posición hacia la autoritativa y se cuenta un strike.
- Todas las acciones con objetivo validan **distancia** contra la posición **del servidor**.
- Cooldowns en servidor. El cliente los muestra, pero el servidor los aplica.
- Ninguna cantidad de oro o ítems llega del cliente como resultado; sólo como *petición*.
- **PvP:** el servidor comprueba la región de atacante **y** víctima con posiciones autoritativas.
  Historial de 500 ms para lag compensation, con margen máximo de 200 ms; pasado ese margen se
  valida contra la posición actual. Un cliente que reporta un RTT inflado no gana alcance.
