# FASE 03 — Personajes

**Modelo:** Sonnet · **Estado:** 📋 planificada

> Diseño ya cerrado en `docs/01-protocolo.md` (opcodes `0x0010–0x0014` / `0x8010–0x8013`,
> máquina de estados `Authenticated → Loading → InWorld`) y `docs/02-esquema-bd.md` (tabla
> `characters`, ya creada por `0001_init.sql` en la Fase 2). Esta fase implementa: el catálogo
> de clases en `content/`, los cinco opcodes de personaje con su repositorio Dapper, y la
> pantalla de selección en Godot. No hay mundo real todavía — eso es la Fase 4 — así que
> "entrar al mundo" en esta fase es una pantalla placeholder que confirma el `WorldEnter`.

## 1. Objetivo

Desde `Authenticated`: listar los personajes de la cuenta (hasta 5 slots), crear uno nuevo en
un slot vacío eligiendo nombre y clase, borrarlo (lógico), y seleccionarlo para entrar — la
sesión pasa a `Loading` con un `WorldEnter`, el cliente confirma con `WorldReady` y pasa a
`InWorld`. Criterio de aceptación completo en §10.

## 2. Contenido — `content/classes/*.json`

Primer uso de `content/` en el repo (no existe todavía ninguna subcarpeta). Tres ficheros,
stats iniciales explícitamente provisionales (los reajusta la Fase 10 con la curva de
progresión real):

```
content/classes/warrior.json
content/classes/mage.json
content/classes/hybrid.json
```

Forma (igual para las tres, valores cambian):

```json
{
  "key": "class.warrior",
  "displayName": "Guerrero",
  "baseStr": 8, "baseInt": 2, "baseVit": 6, "baseDex": 4,
  "baseHp": 120, "baseMp": 20
}
```

`key` coincide con `characters.class_key` (comentario ya en `0001_init.sql`:
`'class.warrior' | 'class.mage' | 'class.hybrid'`). Mago invierte str/int, híbrido reparte
parejo. Sin equipo ni habilidades iniciales — eso es Fase 6/10.

### Resolución de la ruta de contenido

El servidor no tiene hoy ningún mecanismo para localizar `content/` (las migraciones se
embeben en el ensamblado; el contenido, no — `CLAUDE.md §3`, es JSON versionado que se lee tal
cual, para que cambiar un precio no sea una migración). `ContentPaths.Resolve()` sube desde
`AppContext.BaseDirectory` buscando un directorio que contenga `Epimeteo.sln` y añade
`content/`; falla ruidoso si no lo encuentra. Funciona para `dotnet run` y `dotnet test` sin
configuración. **No sirve tal cual para un publish de un solo fichero** — la Fase 5 tendrá que
decidir cómo se despliega `content/` junto al ejecutable (copiarlo al lado, `ContentRoot`
explícito en `appsettings.Production.json`, etc.); se deja anotado, no se resuelve aquí.

`ClassCatalog` carga los tres JSON una vez al arrancar (como `MigrationRunner`, antes de
`app.Run()`) a un `Dictionary<string, ClassDefinition>` en memoria; `classKey` desconocido en
`CharCreate` → error de validación, no excepción.

## 3. Esquema

`characters` ya existe completa desde `0001_init.sql` (Fase 2): slot, nombre, clase, stats,
hp/mp, oro, posición, `deleted_at` para borrado lógico. **No hace falta tocarla**, salvo un
detalle que quedó suelto: el `CHECK` de longitud de `username` en `accounts`
(`username_format`) no tiene equivalente en `characters.name`. Se añade en
`db/migrations/0002_character_name_format.sql`:

```sql
ALTER TABLE characters
    ADD CONSTRAINT character_name_format CHECK (length(name) BETWEEN 3 AND 20);
```

Mismo motivo que en la Fase 2 (`FASE-02-persistencia.md §6`): el `CHECK` de la BD es la última
línea de defensa, la validación real (rango + charset) va en servidor antes de tocar Postgres.

## 4. Mensajes de red

Opcodes ya reservados desde la Fase 1 en `Opcode.cs` (`0x0010–0x0014` / `0x8010–0x8013`);
faltan por tipar en `shared/Epimeteo.Shared/Net/Messages/`, mismas convenciones que siempre
(`[MessagePackObject]`, `[Key(n)]`, sin inicializadores en propiedades `init`):

| Fichero | Opcode | Campos |
|---|---|---|
| `C2SCharListRequest.cs` | `CharListRequest` | (vacío) |
| `C2SCharCreate.cs` | `CharCreate` | `Name string`, `ClassKey string`, `Slot int`, `PaletteIndex byte` |
| `C2SCharDelete.cs` | `CharDelete` | `CharacterId long`, `Confirm bool` |
| `C2SCharSelect.cs` | `CharSelect` | `CharacterId long` |
| `C2SWorldReady.cs` | `WorldReady` | (vacío) |
| `CharacterSummary.cs` | — (tipo compartido, no mensaje) | `Id long`, `Slot int`, `Name string`, `ClassKey string`, `Level int`, `MapKey string`, `PaletteIndex byte` |
| `S2CCharList.cs` | `CharList` | `Characters CharacterSummary[]` |
| `S2CCharCreateResult.cs` | `CharCreateResult` | `Ok bool`, `Code ResultCode`, `Character CharacterSummary?` |
| `S2CCharDeleteResult.cs` | `CharDeleteResult` | `Ok bool`, `Code ResultCode`, `CharacterId long` |
| `S2CWorldEnter.cs` | `WorldEnter` | `MapKey string`, `SpawnX float`, `SpawnY float`, `Facing int`, `MyEntityId long`, `Stats CharacterStats`, `ServerTimeMs long` |
| `CharacterStats.cs` | — (tipo compartido) | `Level int`, `Xp long`, `Str int`, `Int int`, `Vit int`, `Dex int`, `StatPoints int`, `Hp int`, `Mp int`, `Gold long` |

**`PaletteIndex`** (aspecto): con los assets todavía en placeholder (`CLAUDE.md §5`, "nunca
generes ni descargues assets sin que se pida explícitamente") no tiene sentido construir un
editor de apariencia real. Se guarda un único byte 0–3 en `characters.appearance` como
`{"palette": N}` y la vista previa en Godot es un rectángulo de color, no un sprite — sustituible
sin tocar lógica cuando lleguen assets reales, tal como manda la convención de assets.

**`MyEntityId`**: no existe todavía un espacio de IDs de entidad (lo crea la Fase 4 con
`EntitySpawn`/AOI). Se manda `MyEntityId = CharacterId` como valor provisional documentado —
es lo único estable que hay hoy, y la Fase 4 puede cambiarlo sin que esta fase dependa de ello
(el cliente placeholder de esta fase no lo usa para nada, sólo lo loguea).

## 5. Validación y límites

- `Name`: 3–20 (igual que `username`), sólo `[a-zA-Z0-9 _]` — se permite espacio porque un
  nombre de personaje no es un login. Fuera de rango o charset inválido → `ResultCode.NameInvalid`.
- `Name` duplicado entre personajes vivos (`characters_name_uq`) → `ResultCode.NameTaken`.
  Ambos códigos ya existen en el enum compartido desde la Fase 2 (reservados, sin usar hasta
  ahora) — no hace falta añadir ninguno nuevo.
- `ClassKey` no presente en `ClassCatalog` → `ResultCode.NameInvalid` (mismo código que "dato de
  creación mal formado"; no se inventa uno nuevo sólo para esto).
- `Slot` fuera de 0–4, o slot ocupado por un personaje vivo → `ResultCode.SlotOccupied` si está
  ocupado, `ResultCode.NoCharacterSlots` si el número está fuera de rango (desde la perspectiva
  del cliente, ahí no hay ningún slot disponible — la UI real nunca manda un slot fuera de
  0–4, así que esta rama sólo la dispara un cliente manipulado).
- `CharDelete`/`CharSelect` sobre `characterId` que no existe, no es de la cuenta de la sesión,
  o ya está borrado → `ResultCode.CharacterNotFound` en los tres casos (no se filtra si existe
  y es de otra cuenta o no existe en absoluto — mismo motivo que un login inválido no dice cuál
  de los dos datos falló).
- `CharDelete` con `Confirm = false` → se rechaza sin tocar la BD (mismo código,
  `CharacterNotFound`, tratado como "nada que borrar" desde el punto de vista del protocolo; la
  confirmación de verdad es UX en Godot, no un segundo secreto).
- 5 personajes vivos como máximo: ya lo impone `characters_account_slot_uq` (Fase 2) sin
  necesidad de contarlos a mano; un `INSERT` a un slot ocupado por un vivo falla en la BD y el
  repositorio lo traduce a `SlotOccupied`.

## 6. Servidor — piezas nuevas

```
server/Epimeteo.Server/Content/
  ClassDefinition.cs       # POCO del JSON
  ClassCatalog.cs          # carga content/classes/*.json una vez al arrancar
  ContentPaths.cs          # localiza content/ subiendo desde AppContext.BaseDirectory
server/Epimeteo.Server/Persistence/Characters/
  Character.cs             # POCO de la fila
  CharacterRepository.cs   # Dapper: ListByAccount, GetOwned, Create, SoftDelete
  CharacterService.cs      # valida + orquesta, igual rol que AuthService en la Fase 2
```

`SessionMessageHandler` gana los cinco casos, resueltos **en el hilo de red** (familia
`Character`, ya excluida de `IsWorldFamily` desde la Fase 1 — no toca el tick, sólo Postgres):

- `CharListRequest` → `CharacterService.ListAsync(session.AccountId)` → `CharList`.
- `CharCreate` / `CharDelete` → valida, llama al repositorio, responde `*Result`.
- `CharSelect` → verifica propiedad, `session.CharacterId = id`, `session.State = Loading`,
  manda `WorldEnter` con los datos de la fila.
- `WorldReady` → `session.State = InWorld`, log. Nada más: no hay `EntitySpawn` ni entidad de
  mundo todavía, eso empieza en la Fase 4.

`Session` gana `CharacterId` (mismo patrón que `AccountId` en la Fase 2).

## 7. Cliente Godot

- `scenes/CharacterSelect.tscn` + `scripts/Ui/CharacterSelectScreen.cs`: 5 botones de slot: si
  hay personaje muestra nombre/clase/nivel y un botón de borrar (con `ConfirmationDialog` antes
  de mandar `CharDelete Confirm=true`); si está vacío, abre un panel de creación (nombre,
  `OptionButton` de clase, selector de las 4 paletas placeholder). Pide `CharListRequest` al
  entrar en la escena.
- `scenes/WorldPlaceholder.tscn` + `scripts/Ui/WorldPlaceholderScreen.cs`: al recibir
  `WorldEnter` muestra "En `<mapKey>` como `<nombre>` — nivel `<N>`, HP `<hp>`" y manda
  `WorldReady`. Placeholder deliberado — el mundo real es la Fase 4, igual que la Fase 2 se
  conformó con mostrar el `accountId`.
- `LoginScreen.cs` / `RegisterScreen.cs`: el comentario actual
  ("selección de personaje en la Fase 3") se sustituye por la transición real a
  `CharacterSelect` cuando `AuthResult.Ok`.
- `ResultCodeText.cs`: añade `SlotOccupied`, `NoCharacterSlots`, `CharacterNotFound` (los otros
  tres ya están mapeados desde la Fase 2).

## 8. Tests

`tests/Epimeteo.Server.Tests/`, mismo patrón que `AccountRepositoryTests` (Postgres real vía
`PostgresFactAttribute`, se saltan si no hay `ConnectionStrings:Epimeteo`):

- `CharacterRepositoryTests`: crear en slot libre, slot ocupado → excepción/`SlotOccupied`,
  6º personaje (todos los slots llenos) → `SlotOccupied` en el 5º intento fallido tras 5 vivos,
  nombre duplicado → violación de `characters_name_uq`, borrado lógico no libera el slot para
  otro `INSERT` con el mismo slot hasta confirmarlo, `ListByAccount` no devuelve borrados.
- `ClassCatalogTests` (sin Postgres, corre siempre): las tres claves cargan, valores dentro de
  rangos razonables (todos los stats > 0).

## 9. Criterio de aceptación

1. Login → `CharacterSelect` muestra 5 slots vacíos la primera vez.
2. Crear personaje en un slot vacío con nombre válido y una clase → aparece en la lista con los
   stats base de esa clase.
3. Crear con nombre ya usado por otro personaje (de cualquier cuenta) → `NameTaken`.
4. Crear en un slot ya ocupado → `SlotOccupied`.
5. Seleccionar un personaje → sesión pasa a `Loading`, llega `WorldEnter` con los datos de esa
   fila; el cliente manda `WorldReady` → pantalla placeholder, sesión en `InWorld`.
6. Borrar un personaje (con confirmación) → desaparece de la lista; su slot vuelve a estar
   libre para un `CharCreate` nuevo.
7. Cerrar el cliente, volver a entrar (login) → los personajes creados siguen ahí con sus datos.
8. `dotnet test` en verde, incluidos los tests nuevos de `CharacterRepositoryTests` contra la BD
   real ya configurada en la Fase 2.

## 10. Fuera de alcance

Apariencia real (sprites, no paleta placeholder), habilidades o equipo inicial, XP/nivel-up
(Fase 10), cualquier cosa de mundo real — movimiento, mapas, otras entidades visibles (Fase 4),
purga física de personajes borrados (queda `deleted_at`, sin job de limpieza), renombrar
personaje, transferir entre cuentas.
