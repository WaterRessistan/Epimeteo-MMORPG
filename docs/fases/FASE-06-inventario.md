# FASE 06 — Inventario y equipamiento

> Modelo: **Sonnet** (CLAUDE.md §6). El diseño de protocolo, esquema de BD y códigos de error ya
> está cerrado desde las Fases 1–2: los opcodes `0x0030–0x0034`/`0x8030–0x8033`, la tabla
> `item_instances` y los `ResultCode` 300–305 llevan reservados desde entonces sin usarse. Esta
> fase es implementación sobre ese diseño, no diseño nuevo.
>
> Prerrequisito leído: `docs/STATUS.md` (Fase 5 completa, en producción), `docs/00 §3-4`,
> `docs/01` (catálogo de mensajes e inventario), `docs/02 § Ítems, inventario y equipo`.

---

## 1. Objetivo

Que un jugador recién creado tenga un kit inicial, pueda moverlo entre bolsas, equiparlo y ver
sus stats subir, usar un consumible, y tirar algo — todo validado en servidor, persistido sin
tocar Postgres desde el tick, y visible en una UI de Godot (aunque sea fea).

**Fuera de alcance** (para que no se vaya de las manos, mismo criterio que FASE-04 §11):
tiendas y oro gastable (Fase 7), granja/semillas plantables (Fase 8, aunque el *tipo* de ítem
"semilla" sí se define aquí), combate y por tanto daño/defensa derivados (Fase 9), saco de loot
en el suelo (`EntityType.LootBag`, Fase 9 — "tirar" en esta fase destruye el ítem, no lo deja en
el mundo), desgaste de durabilidad, calidad y afijos (columnas ya en el esquema, se leen/escriben
tal cual pero nada las hace variar todavía), comercio entre jugadores, banco compartido
(`container = 4`).

---

## 2. Las seis decisiones de diseño

### D1 — El inventario vive en memoria, autoritativo, cargado una vez al entrar

Igual que la posición (Fase 4): Postgres no es la fuente de verdad *durante* la sesión, es donde
se aparca al salir. `CharSelect` ya carga la fila `characters`; gana también
`ItemRepository.ListByCharacterAsync` (containers 0/1/2/3) y el resultado viaja en
`WorldJoinRequest.Items`, igual que hoy viaja `Hp`/`HpMax`. `PlayerEntity` gana un
`PlayerInventory` (lista de `ItemStack` en memoria) construido en `Zone.Join`.

Las mutaciones (`InvMove`, `InvUse`, `InvDrop`, `Equip`, `Unequip`) llegan por
`IWorldInbox` — ya están clasificadas como `OpcodeFamily.Inventory` y esa familia ya es
"mundo" desde la Fase 1 (`SessionMessageHandler.IsWorldFamily`) — se resuelven en el tick,
contra `PlayerInventory` y el catálogo de ítems, **sin I/O**. Nada nuevo que decidir aquí: es la
misma frontera hilo-red/hilo-tick que ya existe.

### D2 — Persistencia: instantánea completa, no un log de diffs

`CharacterPositionSaver` (Fase 4) manda el valor *actual* completo en cada guardado, no un delta:
por eso `DropOldest` en la cola es seguro — perder una posición vieja no importa si la nueva ya
va detrás. Un inventario es una colección, pero la misma idea aplica si en vez de mandar *lo que
cambió* se manda *el estado completo actual* de los contenedores 0–3 de ese personaje.

`InventorySaver : IHostedService` recibe `InventorySave(CharacterId, IReadOnlyList<ItemStackRow>)`
por un `Channel` (mismo patrón, mismo tamaño de cola, `DropOldest`) y aplica, en una transacción:
`DELETE FROM item_instances WHERE owner_char_id = @id AND container IN (0,1,2,3)` seguido de un
`INSERT` en bloque del estado actual. Idempotente y sin correlación de ids que mantener entre
memoria y BD — el id de Postgres es sólo un detalle de almacenamiento, nunca se referencia desde
el tick (los `ItemStack` en memoria no necesitan id estable entre sesiones, a diferencia de
`characters.id`, que sí se referencia desde fuera).

Se encola tras **cada** mutación que de verdad cambió algo (la frecuencia es bajísima comparada
con los 20 Hz de posición: no hace falta el barrido periódico de `SweepSaves`) y, por si acaso, al
salir del mundo (`Leave`), igual que la posición.

### D3 — Contenedores: la regla la decide `ItemType`, no una lista de excepciones

De `docs/02`: *"una arma sólo entra en la bolsa de armas"*. Formalizado: cada `ItemType` tiene
**un** contenedor no-equipado válido.

| `ItemType` | Contenedor válido (`InvMove`/`InvDrop`/`InvUse`) |
|---|---|
| `Weapon` | 1 (bolsa de armas) |
| `Armor` | 2 (bolsa de armaduras) |
| `Consumable`, `Material`, `Seed` | 0 (general) |

`InvMove` a un contenedor que no es el que le toca al `ItemType` del ítem → rechazado (ver §5).
Contenedor 3 (equipado) no se alcanza por `InvMove`: sólo por `Equip`, que tiene su propia
validación (D4).

### D4 — Equipar: categoría de slot, no slot fijo

`EquipSlot` (12 valores, `docs/02`) es el hueco físico. Un ítem no declara un `EquipSlot` fijo —
declara una `EquipCategory` (`MainHand, OffHand, Head, Chest, Hands, Legs, Feet, Cloak, Ring,
Amulet, Tool`), y una categoría se resuelve a uno o más `EquipSlot`:

- La mayoría de categorías → exactamente un `EquipSlot` (`Head → 2`, `Chest → 3`...).
- `Ring` → `{8, 9}` (anillo1 **o** anillo2). Es el único caso de "uno de varios": el cliente
  manda en `C2SEquip.equipSlot` cuál de los dos, el servidor sólo comprueba que esté en el
  conjunto de la categoría del ítem.

`Equip` con `equipSlot` fuera del conjunto de la categoría del ítem → `ResultCode.NotEquippable`.
Equipar sobre un hueco ocupado es un **intercambio**: el ítem que había vuelve a la bolsa que le
toque por `ItemType` (D3) — si esa bolsa está llena, el `Equip` entero se rechaza (no se pierde el
ítem desequipado en el limbo).

### D5 — Stats derivados: un mensaje nuevo, no reabrir `WorldEnter`

`CharacterStats` (Fase 3) ya dice en su propio comentario *"sin derivados de equipo: eso llega en
la Fase 6"* — pero cambiar su forma reabriría `S2CWorldEnter` y subiría `ProtocolVersion` a 3 sin
necesidad. En vez de eso, los derivados viajan **sólo** en `EquipmentUpdate`
(`0x8032`, ya reservado con el payload exacto que hace falta: *"equipSlot → itemInstance | null,
+ stats derivados recalculados"*). `ProtocolVersion` se queda en 2.

Fórmula (deliberadamente simple: nada de daño/defensa, eso es Fase 9 y no hay combate contra qué
calcularlo todavía):

```
HpMax = clase.baseHp + Σ(bonusHp de cada ítem equipado)
MpMax = clase.baseMp + Σ(bonusMp de cada ítem equipado)
StrEfectivo = personaje.str + Σ(bonusStr de cada ítem equipado)   (ídem Int, Vit, Dex)
```

`Hp`/`Mp` actuales se **clampan** a los nuevos máximos si un desequipo los deja por encima; nunca
se curan de más al equipar (evita el truco de equipar/desequipar para regenerar gratis).

### D6 — Kit inicial: contenido, no código especial

Sin tiendas (Fase 7) ni loot (Fase 9), un personaje recién creado no tendría nada que mover ni
equipar. `content/classes/*.json` gana `startingItems: [{defKey, quantity}]`; `CharacterService`
los inserta en la misma operación que crea el personaje (transacción con `INSERT` de
`characters`, ya existente). Es contenido versionado, no una regla nueva en el servidor.

---

## 3. `content/items/*.json`

Un fichero por ítem (mismo estilo que `content/classes/`), cargados por `ItemCatalog` al arrancar
(mismo patrón que `ClassCatalog`/`MapCatalog`: falla ruidoso si algo no es válido).

```jsonc
{
  "key": "item.iron_sword",
  "displayName": "Espada de hierro",
  "type": "Weapon",              // Weapon | Armor | Consumable | Material | Seed
  "maxStack": 1,
  "equipCategory": "MainHand",   // sólo Weapon/Armor
  "bonusStr": 2, "bonusInt": 0, "bonusVit": 0, "bonusDex": 0,
  "bonusHp": 0, "bonusMp": 0,
  "healAmount": 0                // sólo Consumable, 0 = no cura
}
```

Set mínimo para probar la tubería entera (uno por `ItemType`, cubriendo los dos géneros de bolsa
y una categoría de anillo para probar D4):

`item.iron_sword` (Weapon, MainHand) · `item.wooden_shield` (Weapon†, OffHand — un escudo entra
en la bolsa de armas por regla de negocio aunque no ataque; es lo que dice `docs/02` con "arma" en
sentido amplio de equipable de combate) · `item.leather_chest` (Armor, Chest) ·
`item.copper_ring` (Armor, Ring) · `item.health_potion` (Consumable, apilable, cura) ·
`item.iron_ore` (Material, apilable) · `item.wheat_seed` (Seed, apilable — sin lógica de siembra
todavía, sólo demuestra que el tipo existe).

`content/classes/*.json`: cada clase gana `startingItems` — guerrero con espada+escudo+2
pociones, mago con algo equivalente, híbrido con un surtido menor.

---

## 4. `Shared/Data/` — compartido con el cliente

| Fichero | Qué es |
|---|---|
| `ItemType.cs`, `EquipCategory.cs` | Enums. |
| `ItemDefinition.cs` | POCO del JSON (como `MapDefinition`). |
| `ItemCatalog.cs` | Carga `content/items/*.json`, valida (`equipCategory` presente si y sólo si `type` es Weapon/Armor; `maxStack ≥ 1`; sin claves repetidas). Vive en `Shared` porque el cliente lo necesita para tooltips y para saber qué bolsa le toca a un ítem al dibujar el drag & drop — **la validación de verdad la hace igual el servidor**, esto es sólo para que la UI no tenga que preguntar. |
| `EquipSlots.cs` | `Resolve(EquipCategory) → EquipSlot[]` (D4). Pura, testeable. |

`EquipSlot` ya no existe como concepto nuevo: se añade a `Shared/Simulation` o a `Shared/Data`
junto a lo anterior (los 12 valores de `docs/02`).

---

## 5. Servidor

### `Server/Inventory/` (nuevo)

| Fichero | Qué es |
|---|---|
| `ItemStack.cs` | `class`: `DefKey`, `Container`, `Slot`, `Quantity`, `Durability`, `DurabilityMax`, `Quality`, `Affixes` (json crudo, pasa de largo), `BoundTo`. Nada de id de Postgres (D2). |
| `PlayerInventory.cs` | Lista de `ItemStack` de un jugador + índice `(container,slot) → stack` para validar huecos ocupados en O(1). |
| `InventorySystem.cs` | **Estático**, puro dado `PlayerInventory` + `ItemCatalog`: `TryMove`, `TryUse`, `TryDrop`, `TryEquip`, `TryUnequip`. Devuelve `(bool Ok, ResultCode Code)`, nunca lanza — mismo contrato que `CharacterRepository.CreateAsync` (código de resultado, no excepción, CLAUDE.md §4). |
| `ItemRepository.cs` (en `Persistence/`, no aquí) | Dapper: `ListByCharacterAsync`, y el `INSERT` del kit inicial que usa `CharacterService`. |
| `InventorySave.cs`, `IInventorySink.cs`, `InventorySaver.cs` (en `Persistence/`) | D2. |

### Validaciones (`InventorySystem`)

| Situación | `ResultCode` |
|---|---|
| Slot origen vacío, o de otro jugador (imposible por diseño: siempre opera sobre `PlayerInventory` propio) | `ItemNotFound` |
| Contenedor destino no es el que toca al `ItemType` (D3) | `NotEquippable` (reutilizado: "no va ahí") |
| Slot destino ocupado por algo no apilable con el origen, o apilado ya al máximo | se completa lo que quepa y el resto se queda donde estaba (comportamiento estándar de "mover a pila", no error) |
| `quantity` de `InvMove`/`InvDrop` mayor que la que hay | `NotEnoughItems` |
| `Equip` con `equipSlot` fuera de la categoría del ítem | `NotEquippable` |
| `Equip` de un `ItemType` que no es Weapon/Armor | `NotEquippable` |
| Intercambio de `Equip` sin sitio en la bolsa que le toca al que se desequipa (D4) | `InventoryFull` |
| `InvUse` sobre algo sin `healAmount` (no consumible de curación) | `NotEquippable` (reutilizado; no hay un código "NotUsable" dedicado y añadir uno nuevo reabriría el enum cerrado sin necesidad — es el código más cercano en significado: "esto no se usa así") |

Fallo de validación → **no se manda nada de vuelta especial**: el estado en memoria no cambió,
así que no hay `InventoryDelta`/`EquipmentUpdate` que mandar (el catálogo de mensajes de Fase 1 no
reservó un `InvResult` dedicado, a diferencia de `ShopResult` — es la interpretación correcta del
protocolo ya cerrado, no una decisión nueva). Se manda `SystemMessage` (`0x8071`, ya reservado,
sin tipo todavía — se crea `S2CSystemMessage.cs` en esta fase, es la primera que lo necesita) con
severidad *Info* y una clave i18n derivada del `ResultCode`, sólo para que la UI pueda avisar
"eso no cabe ahí" en vez de quedarse muda. Un cliente honesto no debería intentar un movimiento
inválido nunca (valida con el mismo `ItemCatalog`), así que esto es UX, no seguridad.

### Cambios en piezas existentes

- **`ItemRepository`**: `ListByCharacterAsync(characterId)` (containers 0–3).
- **`CharacterService`/`CharacterRepository.CreateAsync`**: inserta `startingItems` de la clase
  en la misma operación que crea el personaje.
- **`WorldJoinRequest`**: gana `IReadOnlyList<ItemStack> Items`.
- **`PlayerEntity`**: gana `PlayerInventory Inventory`.
- **`Zone.Join`**: tras `SendZoneFlags`, manda `InventoryFull` (containers 0/1/2) y
  `EquipmentUpdate` (container 3 + stats derivados) — una vez, es el "aquí tienes tu inventario"
  inicial que pide `docs/01` para `InventoryFull`.
- **`GameWorld.DrainMessages`**: cinco casos nuevos (`InvMove`, `InvUse`, `InvDrop`, `Equip`,
  `Unequip`), mismo patrón que `HandleInput`: decodificar, si falla `Kick(ProtocolError)`, si
  vale, resolver contra `zone.FindBySession(...)`. Al final de cada uno con éxito: mandar
  `InventoryDelta` (y `EquipmentUpdate` si tocó equipo) y encolar en `InventorySaver`.
- **`Program.cs`**: registra `ItemCatalog` (como `ClassCatalog`/`MapCatalog`), `ItemRepository`,
  `InventorySaver` (`IHostedService` + `IInventorySink`, mismo orden de arranque/parada que
  `CharacterPositionSaver`).
- **`/status`**: gana `pendingInventorySaves` junto a `pendingSaves`.

---

## 6. Protocolo — sin abrir el enum, sólo tipar lo reservado

Nada nuevo en `Opcode.cs`/`ResultCode.cs`: los 9 opcodes y los 6 `ResultCode` de inventario ya
estaban desde la Fase 1. Se crean los `record` de MessagePack que faltan en
`Shared/Net/Messages/`:

`C2SInvMove`, `C2SInvUse`, `C2SInvDrop`, `C2SEquip`, `C2SUnequip` ·
`ItemStackInfo` (tipo compartido: `defKey, container, slot, quantity, durability, durabilityMax,
quality, boundTo` — sin `affixes` en el payload de red por ahora: nadie los genera todavía, mismo
criterio que "no calcules lo que nada consume") ·
`S2CInventoryFull` (`items: ItemStackInfo[]`) ·
`S2CInventoryDelta` (`changes: InventoryChangeEntry[]`, con `InventoryChangeEntry { container,
slot, item: ItemStackInfo? }` — `item = null` significa "este hueco quedó vacío") ·
`S2CEquipmentUpdate` (`equipped: ItemStackInfo[]` con `slot` = `EquipSlot`, más `hpMax, mpMax,
strEffective, intEffective, vitEffective, dexEffective`) ·
`S2CSystemMessage` (`severity: byte, key: string, args: string[]`).

---

## 7. Cliente Godot

Sin arte (CLAUDE.md §5): rejillas de rectángulos con el nombre del ítem y la cantidad en texto,
igual que `WorldRenderer` dibuja entidades sin sprites. El objetivo es la tubería correcta
(arrastrar manda el opcode que toca), no la estética.

| Fichero | Qué es |
|---|---|
| `client/scripts/Inventory/InventoryState.cs` | Espejo cliente de `PlayerInventory`: recibe `InventoryFull`/`InventoryDelta`/`EquipmentUpdate` de `NetClient` y mantiene el estado para dibujar. Sin predicción — a diferencia del movimiento, aquí no hay coste perceptible en esperar la confirmación del servidor antes de mover el icono. |
| `client/scripts/Ui/InventoryScreen.cs` + `.tscn` | Overlay (tecla `I`) con 3 pestañas (General/Armas/Armaduras) de rejilla de slots. |
| `client/scripts/Ui/EquipmentPanel.cs` + `.tscn` | Lista de los 12 `EquipSlot` con lo que hay puesto (o vacío) y los stats derivados debajo. |
| `client/scripts/Ui/ItemSlot.cs` | Un slot: `_GetDragData`/`_CanDropData`/`_DropData` (API nativa de Godot para drag & drop) — al soltar, manda `C2SInvMove` o `C2SEquip` según origen/destino. Tooltip al pasar el ratón (`_MakeCustomTooltip`) con nombre/cantidad/bonus. |

`NetClient` gana `InventoryFullReceived`, `InventoryDeltaReceived`, `EquipmentUpdateReceived`,
`SystemMessageReceived`, y `SendInvMove/InvUse/InvDrop/Equip/Unequip(...)`.

**No verificable en este servidor** (mismo motivo que la escena de mundo en la Fase 4: headless,
sin Godot instalado). Se compila y se deja listo; la prueba visual queda pendiente de una sesión
con entorno gráfico.

---

## 8. Tests

**`Epimeteo.Shared.Tests`**
- `EquipSlotsTests`: cada categoría resuelve a su slot; `Ring` resuelve a `{8,9}`.
- `ItemCatalogTests`: catálogo válido carga · Weapon/Armor sin `equipCategory` → error de carga ·
  claves repetidas → error · `maxStack < 1` → error.

**`Epimeteo.Server.Tests`**
- `InventorySystemTests` (el grueso, todo sin BD ni tick, sobre `PlayerInventory` en memoria):
  mover a hueco vacío · mover a hueco con el mismo ítem apila hasta el máximo y deja el resto ·
  mover una espada a la bolsa de armaduras → `NotEquippable` · dividir pila con `quantity` parcial ·
  tirar más de lo que hay → `NotEnoughItems` · equipar arma en `MainHand` → aparece en container 3,
  slot 0 · equipar sobre hueco ocupado intercambia y el viejo vuelve a su bolsa ·
  intercambio sin sitio en la bolsa → `InventoryFull`, nada cambia (ítem no se pierde) · anillo en
  `equipSlot=8` y en `9` ambos válidos · anillo en `equipSlot=2` (Head) → `NotEquippable` ·
  desequipar clampa `Hp`/`Mp` actuales a los nuevos máximos si hacía falta · usar poción de
  curación reduce la cantidad (o borra el stack si llega a 0) y sube `Hp` sin pasar de `HpMax`.
- `ItemRepositoryTests` (`PostgresFact`): `ListByCharacterAsync` devuelve lo esperado tras crear
  un personaje con `startingItems`.
- `InventorySaverTests`: una `InventorySave` deja Postgres con exactamente ese conjunto (probado
  con dos saves consecutivas con contenido distinto — la segunda reemplaza a la primera, D2).

---

## 9. Verificación sin Godot

Se amplía `tools/Epimeteo.WorldBot` (o `SmokeClient`, lo que encaje mejor con el flujo InWorld ya
existente) con el flujo de inventario tras `WorldReady`: recibe `InventoryFull`/`EquipmentUpdate`
del kit inicial → mueve la poción a otro slot del general → equipa la espada → comprueba que
`EquipmentUpdate` refleja `strEffective` subido → intenta equipar la espada en `Head` → confirma
que **no** cambia nada y llega `SystemMessage` → usa una poción → tira un material.

---

## 10. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde con los tests nuevos.
2. Un personaje nuevo entra al mundo y `InventoryFull`/`EquipmentUpdate` reflejan el kit inicial
   de su clase.
3. Mover, apilar, dividir, tirar, usar y equipar/desequipar funcionan de punta a punta contra el
   servidor real (verificado por la herramienta de §9), con los contenedores respetando las
   reglas de `ItemType` y `EquipCategory`.
4. Los stats derivados (`HpMax`, `MpMax`, stats efectivos) cambian al equipar/desequipar y se ven
   en `EquipmentUpdate`.
5. Reiniciar el servidor con un jugador dentro no pierde su inventario (mismo criterio que la
   posición en la Fase 4), comprobado en `psql`.
6. El cliente Godot compila sin warnings. Si hay entorno gráfico, se ve la UI y el drag & drop
   funciona; si no, queda anotado como pendiente (mismo criterio que FASE-04 §13.7).
