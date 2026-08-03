# CLAUDE.md — Epimeteo MMORPG

> Lee **sólo** este archivo + el doc de la fase que te toque (`docs/fases/FASE-XX-*.md`) + `docs/STATUS.md`.
> No leas el resto de `docs/` salvo que la fase lo pida. Esto es intencional para ahorrar tokens.

## 1. Qué es este proyecto

**Epimeteo**: MMORPG 2D top-down, estética pixel art 16 bits (referencia: Stardew Valley / Zelda ALTTP).
Pueblo con granja, tiendas, armero, inventario separado de armas y armaduras, 3 clases
(guerrero / mago / híbrido), hasta 5 personajes por cuenta, y combate contra monstruos.

**PvP por zonas:** el pueblo es zona segura; las zonas de farmeo tienen PvP activo.
**Granja en tiempo real:** los cultivos tardan ~3 días **reales** en madurar.

**Arquitectura cliente-servidor autoritativa desde el día 1.** No hay modo single-player.
El servidor es la única fuente de verdad. El cliente es un terminal tonto con predicción.

## 2. Stack (decidido, no reabrir sin motivo)

| Capa | Tecnología |
|---|---|
| Cliente | **Godot 4.5 (.NET / C#)** |
| Servidor | **.NET 8 (C#) headless**, sin dependencia de Godot |
| Código compartido | Librería de clase `net8.0` referenciada por cliente y servidor |
| Transporte | **WebSocket binario** (`wss://`) detrás de proxy inverso |
| Serialización | **MessagePack-CSharp** con claves explícitas |
| Base de datos | **PostgreSQL 16** + **Dapper** + **Npgsql** (NO EF Core) |
| Migraciones | Ficheros `.sql` numerados + **DbUp** |
| Caché / presencia | **Redis** (sólo si se llega a la Fase 14; antes, memoria del proceso) |
| Hash de contraseñas | **Argon2id** (`Konscious.Security.Cryptography.Argon2`) |
| Despliegue | Ubuntu 24.04 + systemd + proxy inverso (subdominio, TLS) |
| Logging | Serilog → stdout + fichero rotativo |

### Presentación (fijado en Fase 0)
- **Tile: 16×16 px.** Es el formato más común en packs CC0 (Kenney, itch.io) y el de las referencias.
- **Resolución base: 480×270**, escalada a entero (×4 = 1080p exacto). Se ven ~30×17 tiles.
- Personajes: 16×32 px (2 tiles de alto), pivote en los pies, Y-sort activado.

### Puertos
80/443/8080 están ocupados en el host de producción. El juego escucha **sólo en loopback**:

- `127.0.0.1:5100` → WebSocket de juego (`/ws`)
- `127.0.0.1:5101` → API HTTP (login, versión, estado)
- Público: `https://<subdominio>/` y `wss://<subdominio>/ws` a través del proxy del 443

## 3. Estructura de carpetas

```
mmorpg/
├── CLAUDE.md
├── Epimeteo.sln
├── docs/
│   ├── STATUS.md                  # estado vivo del proyecto — SE ACTUALIZA CADA SESIÓN
│   ├── 00-arquitectura.md
│   ├── 01-protocolo.md
│   ├── 02-esquema-bd.md
│   ├── 03-roadmap-fases.md
│   └── fases/FASE-XX-nombre.md
├── shared/Epimeteo.Shared/        # protocolo, DTOs, enums, simulación compartida
│   ├── Net/                       # Opcode, envelope, mensajes C2S y S2C
│   ├── Simulation/                # movimiento, colisión, fórmulas — código IDÉNTICO en ambos lados
│   └── Data/                      # POCOs de definiciones de contenido
├── server/Epimeteo.Server/
│   ├── Net/                       # listener WS, sesiones, rate limit
│   ├── World/                     # zonas, tick loop, AOI, entidades
│   ├── Systems/                   # combate, granja, tiendas, inventario, loot
│   ├── Persistence/               # repositorios Dapper, cola de guardado
│   └── Program.cs
├── client/                        # proyecto Godot 4.5 (project.godot en esta carpeta)
│   ├── scenes/                    # .tscn
│   ├── scripts/                   # .cs del cliente
│   ├── assets/                    # ver §5
│   └── Epimeteo.Client.csproj
├── content/                       # DATOS DE JUEGO en JSON — fuente de verdad, versionada en git
│   ├── items/  monsters/  crops/  shops/  classes/  maps/
├── db/migrations/                 # 0001_init.sql, 0002_....sql
├── deploy/                        # unit systemd, config del proxy, scripts de backup
└── tools/                         # validadores de contenido, generadores, load tester
```

**Regla clave:** las *definiciones* de contenido (ítems, monstruos, cultivos, tiendas, clases)
viven en `content/*.json` versionado en git, **no en la BD**. La BD sólo guarda **estado mutable
de jugador** y referencia las definiciones por `key` (string estable, ej. `weapon.iron_sword`).
Cambiar el precio de una espada = editar un JSON, no una migración.

## 4. Convenciones de código

- C# 12, `nullable` habilitado, `ImplicitUsings` habilitado, warnings como errores en `shared/` y `server/`.
- Namespaces: `Epimeteo.Shared.*`, `Epimeteo.Server.*`, `Epimeteo.Client.*`. File-scoped namespaces.
- Nombres: `PascalCase` para tipos/métodos/propiedades, `_camelCase` para campos privados,
  `camelCase` para locales/parámetros. Constantes `PascalCase`.
- Un tipo público por fichero; el nombre del fichero = nombre del tipo.
- `record` para mensajes de red y DTOs inmutables; `class` para entidades con estado; `struct`
  sólo para valores pequeños (`TilePos`, `Vec2`).
- **Async:** el bucle de simulación es **síncrono y de un solo hilo por zona**. La E/S (red, BD)
  es async y cruza al hilo de simulación mediante colas concurrentes. Nunca hagas `await` de BD
  dentro del tick.
- **Nada de `float` para dinero ni cantidades.** Oro = `long`. Cantidades = `int`.
- Tiempo de servidor = `long` en milisegundos monotónicos desde el arranque (`ServerClock.NowMs`),
  no `DateTime.Now`. Para persistencia sí `timestamptz` UTC.
- SQL: snake_case, tablas en plural (`characters`, `item_instances`), PK `id`, FK `<tabla_sing>_id`.
- Logs: Serilog estructurado, `Log.Information("Player {CharacterId} bought {ItemKey}", ...)`.
  Nunca concatenar strings en logs.
- Errores esperados (login inválido, oro insuficiente) → códigos de resultado en el mensaje de
  respuesta, **no excepciones**. Excepciones sólo para fallos de programación.
- Tests: xUnit en `tests/`. Obligatorios para `Epimeteo.Shared/Simulation/` y para las
  fórmulas de economía/combate. El resto, opcional.

### Reglas de seguridad no negociables
- El cliente **nunca** envía posiciones, daño, oro ni cantidades resultantes. Sólo **intenciones**
  (dirección de input, "quiero comprar 3 de X", "quiero atacar a la entidad 42").
- Toda petición se valida en servidor contra el estado del servidor: distancia, cooldown,
  propiedad del ítem, stock, oro.
- **PvP:** el flag de zona lo decide el servidor a partir del mapa y la posición autoritativa
  del atacante **y** de la víctima. El cliente sólo lo usa para pintar el aviso de "zona hostil".
- Rate limit por sesión y por opcode. Mensaje malformado o fuera de estado → desconexión.

## 5. Convención de assets (CC0 / IA)

Los assets son **placeholder hasta nuevo aviso**. Nunca generes ni descargues assets sin que se
pida explícitamente. Nunca hardcodees una ruta de sprite en la lógica de juego: el contenido
JSON declara la clave visual y el cliente la resuelve con un atlas registry.

```
client/assets/
├── sprites/
│   ├── characters/   tilesets/   items/   monsters/   fx/   ui/
├── audio/  fonts/
└── ATTRIBUTIONS.md     # OBLIGATORIO: una línea por pack
```

- Todo el arte es de **tile 16×16**. Un pack de 32×32 no se mezcla: o se reescala el proyecto
  entero o se descarta el pack.
- Nomenclatura: `snake_case`, sin espacios ni acentos. `iron_sword_16.png`, `village_tileset_a.png`.
- Cada pack va en su subcarpeta con el nombre del origen: `sprites/tilesets/kenney_roguelike/`.
- Assets generados con IA → subcarpeta `_ai_placeholder/`. Todo lo que esté ahí es **descartable**
  y debe poder sustituirse sin tocar código.
- `ATTRIBUTIONS.md`: `| carpeta | fuente/URL | autor | licencia | fecha |`. CC0 también se anota.
- Import de Godot para todo pixel art: filtro **Nearest**, mipmaps **off**,
  `snap_2d_transforms_to_pixel` activado, estirado `canvas_items` + `keep` en 480×270.
- El servidor **no conoce assets**. Si el servidor necesita saber el tamaño de una hitbox,
  eso va en `content/`, no en el PNG.

## 6. Política de modelos (control de gasto)

Una fase = una sesión de Claude Code. Al terminar, actualiza `docs/STATUS.md` y cierra la sesión.

| Modelo | Cuándo |
|---|---|
| **Opus** | Diseño, protocolo de red, netcode (predicción/reconciliación/AOI), esquema y migraciones de BD, combate y PvP, seguridad/anticheat. Fases **0, 1, 4, 9, 13**. |
| **Sonnet** | Implementación sobre un diseño ya cerrado: CRUD, repositorios, UI de Godot, sistemas de juego con reglas ya escritas, deploy, contenido, tests. Fases **2, 3, 5, 6, 7, 8, 10, 11, 12, 15**. |
| **Haiku** | Trabajo mecánico: renombrados, rellenar JSON de contenido, boilerplate repetitivo, formateo. |

Reglas de ahorro:
1. No leas ficheros "por contexto". Lee sólo lo que vas a modificar.
2. No expliques lo que ya está en este CLAUDE.md.
3. No refactorices código de fases anteriores salvo que la fase lo pida.
4. Si una fase se te va de las manos, **para**, escribe el estado en `docs/STATUS.md` y dilo.
   Es más barato abrir una sesión nueva que arrastrar un contexto enorme.
5. Nada de resúmenes largos al final. Un párrafo y la lista de ficheros tocados.

## 7. Comandos

```bash
dotnet build Epimeteo.sln
dotnet run --project server/Epimeteo.Server            # servidor local, :5100 / :5101
dotnet test
dotnet run --project tools/Epimeteo.ContentValidator   # valida content/*.json
# Cliente: abrir client/project.godot en Godot 4.5 .NET
```

## 8. Estado

Ver `docs/STATUS.md`. **Actualízalo al final de cada sesión.**
