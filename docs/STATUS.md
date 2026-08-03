# STATUS — Epimeteo MMORPG

**Última actualización:** 2026-08-03 · **Fase actual:** 3 CERRADA (personajes) → arranca Fase 4

## Estado

| Área | Estado |
|---|---|
| Diseño y arquitectura | ✅ Cerrado (`docs/00`, `01`, `02`, `03`) |
| Repositorio git | ✅ Fases 0, 1 y 2 commiteadas; Fase 3 lista para commitear |
| Solución .NET | ✅ `Epimeteo.sln` (Shared + Server + Server.Tests + Shared.Tests + tools) |
| Protocolo | ✅ Envelope, opcodes, tabla de estados, códec MessagePack; auth + los 5 opcodes de personaje (`CharList*`, `CharCreate`, `CharDelete`, `CharSelect`, `WorldReady`/`WorldEnter`) tipados |
| Servidor | ✅ Kestrel 5100/5101, sesiones, rate limit, tick 20 Hz, `/status`, migraciones DbUp, auth; + `CharacterService`/`CharacterRepository`, `ClassCatalog` (carga `content/classes/*.json` al arrancar), transición `Authenticated → Loading → InWorld` |
| Cliente Godot | ✅ Conecta, handshake, login/registro; + `CharacterSelect` (crear/borrar/entrar, 5 slots) y `WorldPlaceholder` tras `WorldEnter` (no probado con editor Godot en esta sesión — servidor de producción es headless, ver "Verificación Fase 3") |
| Tests | ✅ 16/16 compartidos + **23/23 servidor en verde** (0 saltados) |
| Base de datos | ✅ Postgres 16.14; `0001_init.sql` + `0002_character_name_format.sql` aplicadas |
| Contenido (`content/`) | ✅ `content/classes/{warrior,mage,hybrid}.json` (primer uso de la carpeta) |
| Despliegue | ❌ |

## Entorno

- **Esta sesión trabajó directamente en el servidor de producción** (Ubuntu 24.04.3 LTS, `arm64`,
  hostname `vnic-mario`, usuario `ubuntu`), no en el WSL2 de desarrollo. `sudo` con TTY disponible.
- **.NET SDK 8.0.129** instalado por `apt` (paquete nativo `dotnet-sdk-8.0` del repo de Ubuntu
  24.04, no el script de Microsoft) → `/usr/bin/dotnet`. Antes de esta sesión no había `dotnet`
  en este servidor.
- **PostgreSQL 16.14** — ya estaba instalado y corriendo (`postgresql@16-main.service`, activo)
  antes de empezar esta sesión; no hizo falta `apt install`. El rol `epimeteo` y la BD `epimeteo`
  también existían ya (de un intento anterior no documentado), pero sin contraseña conocida →
  se resetió con `ALTER ROLE epimeteo WITH PASSWORD ...` (generada con `openssl rand`, guardada
  sólo en `appsettings.Development.json`, gitignored). Escucha sólo en `127.0.0.1:5432`.
- No hay Godot instalado en este servidor (es headless). El criterio de aceptación §12 puntos
  3–7 (registro/login/rate-limit) se verificó con `tools/Epimeteo.SmokeClient --lento` en vez
  del cliente Godot manual — cubre exactamente los mismos pasos hablando el protocolo real
  contra el servidor real y Postgres real. Godot queda pendiente de probar cuando se abra un
  editor gráfico (fuera de alcance en un servidor de producción sin sesión de escritorio).
- Puertos 80, 443, 8080 y 8443 **confirmado ocupados** por otros servicios en este servidor:
  80/443 → `nginx`, 8080 → `uvicorn`, 8443 → `node`. El juego sigue en loopback `5100`/`5101`
  como estaba previsto (`CLAUDE.md §2`), sin conflicto. Pendiente de decidir en la Fase 5 cómo
  cuelga el subdominio del juego del proxy existente sin tocar los otros tres servicios.

## Hecho en la Fase 1

Detalle completo y verificación en `docs/fases/FASE-01-esqueleto.md`.

- `shared/Epimeteo.Shared`: `Opcode` (catálogo entero de `docs/01`, ninguno reutilizable),
  `OpcodeTable` con familia y estados legales por opcode, `FrameCodec`, `ResultCode`,
  `KickReason`, `SessionState`, `ServerClock` monotónico, los 5 mensajes del handshake.
- `server/Epimeteo.Server`: WS en `:5100`, HTTP `/version` y `/status` en `:5101`, cada puerto
  con su superficie aislada; `Session` con bucles de lectura/escritura separados; rate limiter
  por familia con strikes; `GameLoop` a 20 Hz con compensación de deriva y métricas de tick.
- `client/`: proyecto Godot 4.5 (480×270, Nearest, snap a píxel), `NetClient` con `WebSocketPeer`,
  pantalla de conexión con estado y RTT.
- `tools/Epimeteo.SmokeClient`: verifica el handshake y las reglas de protocolo sin abrir Godot.

## Decisiones tomadas en la Fase 1

- **`Hello` y `Ping` se resuelven en el hilo de red**, no en el tick: no tocan estado de mundo y
  la cola del tick les añadiría hasta 50 ms que falsearían el RTT. Los opcodes de mundo sí cruzan
  por `IWorldInbox` (cola concurrente que **copia** el payload); el consumidor llega en la Fase 4.
- **Sin catch-up de ticks.** Si el bucle se retrasa más de un tick, se descarta el desfase en vez
  de acelerar la simulación: acelerar duplicaría el desplazamiento por tick y rompería la
  predicción del cliente.
- **Barrido de timeouts dentro del bucle de tick** (1 vez/s), no en un hilo aparte.
- **MessagePack 3.1.8.** La rama 2.5 tiene 11 CVEs y con warnings como errores ni restaura.
  El analizador MsgPack017 obliga a que **los mensajes de red no lleven inicializadores en
  propiedades `init`**: los campos opcionales de tipo referencia van como `string?`.
- **Dos soluciones**: la de la raíz y `client/Epimeteo.Client.sln` para Godot.
- Frames de texto y frames > 16 KB cierran la conexión, igual que los opcodes fuera de estado.

## Hecho en la Fase 2 — CERRADA

Detalle completo del diseño en `docs/fases/FASE-02-persistencia.md`. Código escrito en sesiones
anteriores; en esta sesión se verificó de punta a punta contra Postgres real en producción.

- `db/migrations/0001_init.sql`: las cinco tablas de `docs/02-esquema-bd.md`
  (`accounts`, `account_sessions`, `login_attempts`, `characters`, `item_instances`).
- `server/Epimeteo.Server/Persistence/`: `NpgsqlConnectionFactory`, `MigrationRunner` (DbUp,
  scripts embebidos, corre una vez al arrancar antes de `app.Run()`), y en `Accounts/`:
  `PasswordHasher` (Argon2id, formato PHC autocontenido), `AccountRepository`,
  `LoginAttemptRepository`, `SessionTokenService` (token de 32 bytes, sólo se guarda el
  SHA-256), `AuthService` (orquesta Login/Register), `AuthOutcome`.
- `shared/Epimeteo.Shared/Net/Messages/`: `C2SLogin`, `C2SRegister`, `S2CAuthResult`.
  `ResultCode.PasswordInvalid = 105` añadido al enum.
- `SessionMessageHandler` resuelve `Login`/`Register` en el hilo de red (igual que `Hello`/`Ping`
  en la Fase 1), sin tocar el tick de mundo.
- `client/`: `scenes/Login.tscn` + `LoginScreen.cs`, `scenes/Register.tscn` +
  `RegisterScreen.cs`, `ResultCodeText.cs` (mapeo `ResultCode` → texto en español).
  `ConnectScreen` transiciona a `Login` tras el `HelloAck` en vez de quedarse mostrando RTT.
- `tests/Epimeteo.Server.Tests/` (proyecto nuevo): `PasswordHasherTests` (6 tests, corren
  siempre, en verde). `AccountRepositoryTests` y `LoginAttemptRepositoryTests` (7 tests) usan
  `PostgresFactAttribute` — se saltan si `ConnectionStrings:Epimeteo` no está configurada; desde
  esta sesión hay Postgres real y `appsettings.Development.json`, así que corren y pasan.
- `appsettings.Development.json.example` con plantilla de cadena de conexión. El real,
  `appsettings.Development.json`, creado en esta sesión con la contraseña de desarrollo
  (sigue en `.gitignore`, no se sube a git).

### Verificación Fase 2 (esta sesión, en el servidor de producción)

Criterio de aceptación completo de `FASE-02-persistencia.md §12`, todo en verde:

1. Postgres 16.14 ya corriendo; `psql` conecta con el rol `epimeteo` (contraseña reseteada esta
   sesión, ver "Entorno").
2. Primer arranque de `dotnet run --project server/Epimeteo.Server`: log "1 migraciones
   aplicadas (o ya al día)", crea `schemaversions` + las 5 tablas de `0001_init.sql`. Segundo
   arranque (proceso limpio): log "0 migraciones aplicadas (o ya al día)" — confirma
   idempotencia.
3–7. Verificado con `tools/Epimeteo.SmokeClient --lento` (no hay Godot en este servidor
   headless; el SmokeClient habla el protocolo real y cubre exactamente los mismos pasos):
   registro nuevo → `Ok` + `SessionToken`; login en otra conexión con las mismas credenciales →
   `Ok`; login con usuario inexistente → `InvalidCredentials`; registro con username repetido →
   `AccountAlreadyExists`; 6 logins fallidos seguidos → `RateLimited` en el 6º. 18/18
   comprobaciones del SmokeClient en verde (incluye además todo lo heredado de la Fase 1:
   handshake, versión incorrecta, opcode fuera de estado, frame grande, frame de texto, timeout
   sin `Hello`).
8. `dotnet test`: **16/16 compartidos + 13/13 servidor**, 0 saltados (antes se saltaban 7 por
   falta de Postgres).

Confirmado también en la BD tras la corrida: `accounts` y `login_attempts` con filas reales
creadas por el SmokeClient, y en el log del servidor las líneas `Sesión N autenticada como
cuenta M` correspondientes.

Godot en sí (la parte visual del criterio §12.3) no se probó con el editor porque este servidor
no tiene entorno gráfico — pendiente de una sesión con acceso a Godot si se quiere el visto
bueno manual además del automático.

## Hecho en la Fase 3 — CERRADA

Plan completo en `docs/fases/FASE-03-personajes.md` (escrito y verificado en esta misma sesión,
justo después de cerrar la Fase 2).

- `content/classes/{warrior,mage,hybrid}.json`: primer contenido versionado del repo, stats
  base provisionales (los reajusta la Fase 10).
- `db/migrations/0002_character_name_format.sql`: `CHECK` de longitud en `characters.name`,
  pareja del `username_format` de `accounts` que se había quedado suelto en la Fase 2.
- `shared/Epimeteo.Shared/Net/Messages/`: los 5 mensajes C2S (`CharListRequest`, `CharCreate`,
  `CharDelete`, `CharSelect`, `WorldReady`) y sus S2C (`CharList`, `CharCreateResult`,
  `CharDeleteResult`, `WorldEnter`) más los tipos compartidos `CharacterSummary`/`CharacterStats`.
  Sin opcodes nuevos: los 9 ya estaban reservados desde la Fase 1.
- `server/Epimeteo.Server/Content/`: `ClassCatalog` (carga `content/classes/*.json` una vez al
  arrancar, como `MigrationRunner`), `ContentPaths` (localiza `content/` subiendo desde
  `AppContext.BaseDirectory` hasta `Epimeteo.sln` — no sirve tal cual para un `publish` de un
  solo fichero, pendiente en la Fase 5).
- `server/Epimeteo.Server/Persistence/Characters/`: `CharacterRepository` (Dapper, distingue
  `SlotOccupied`/`NameTaken` por el nombre del índice único que salta), `CharacterService`
  (valida y orquesta, mismo rol que `AuthService`).
- `SessionMessageHandler` gana los 5 casos, resueltos en el hilo de red (familia `Character`, no
  toca el tick). Decisión tomada al implementar: `CharSelect` a un personaje inexistente/ajeno
  no tiene opcode de fallo dedicado en el protocolo cerrado (sólo `WorldEnter` en éxito) — se
  trata como un dato imposible con un cliente honesto y se resuelve con `Kick`, igual que
  cualquier otra violación de protocolo, en vez de inventar un mensaje nuevo.
- `client/`: `CharacterSelect.tscn`/`CharacterSelectScreen.cs` (5 slots, crear/borrar con
  confirmación/entrar), `WorldPlaceholder.tscn`/`WorldPlaceholderScreen.cs` (placeholder tras
  `WorldEnter`, manda `WorldReady`). La "apariencia" es sólo un índice de paleta 0–3 (rectángulo
  de color): los assets siguen en placeholder, no se generó ni descargó arte.
- `tests/Epimeteo.Server.Tests/`: `CharacterRepositoryTests` (5, contra Postgres real) y
  `ClassCatalogTests` (5, sin Postgres).

### Verificación Fase 3 (esta sesión, en el servidor de producción)

Criterio de aceptación de `FASE-03-personajes.md §9`, todo en verde:

- `dotnet test`: **16/16 compartidos + 23/23 servidor**, 0 saltados.
- `tools/Epimeteo.SmokeClient --lento` (ampliado esta sesión con el flujo de personajes; sigue
  sin haber Godot en este servidor headless): registro → lista vacía → crear en slot vacío →
  nombre repetido → `NameTaken` → slot ocupado → `SlotOccupied` → `CharSelect` → `WorldEnter`
  con el mapa y stats de guerrero → `WorldReady` sin caer la sesión (`Ping` sigue respondiendo)
  → borrar sin confirmar (no borra) → borrar confirmado → slot liberado admite personaje nuevo
  → cerrar conexión, reabrir con login, el personaje sigue ahí. **33/33 comprobaciones en verde**
  (incluye todo lo heredado de las Fases 1 y 2).
- Detalle de infraestructura encontrado al verificar: el cupo de 5 intentos/minuto por IP
  (Fase 2) es compartido por todo el `SmokeClient`, que ya lo agota a propósito en la prueba de
  `RateLimitDeLogin`; las pruebas de personajes necesitan `Register`/`Login` reales después, así
  que el SmokeClient espera 65 s a que la ventana se libere antes de esa sección. Alarga la
  ejecución pero no es un problema del servidor, es la ventana deslizante funcionando como debía.

## Siguiente sesión

**Empezar la Fase 4 — Mundo y movimiento autoritativo (Opus).** Ver `docs/03-roadmap-fases.md`;
escribir primero `docs/fases/FASE-04-*.md` con el plan antes de implementar (regla de
`docs/03-roadmap-fases.md § Cómo abrir cada sesión`). Es la fase más importante del proyecto:
predicción, reconciliación, interpolación y AOI. `MyEntityId` en `S2CWorldEnter` hoy vale
`CharacterId` como valor provisional (Fase 3) — la Fase 4 puede darle un espacio de IDs propio
sin que nada dependa de que sea así.

Recordatorio de entorno: este servidor de producción ya tiene `dotnet`, Postgres y el rol
`epimeteo` listos; no hace falta repetir la instalación en próximas sesiones aquí. La contraseña
de desarrollo vive sólo en `server/Epimeteo.Server/appsettings.Development.json` (gitignored) —
si se pierde, se resetea con `sudo -u postgres psql -c "ALTER ROLE epimeteo WITH PASSWORD '...';"`.

### Comandos útiles

```bash
dotnet build Epimeteo.sln && dotnet test
dotnet run --project server/Epimeteo.Server
dotnet run --project tools/Epimeteo.SmokeClient -- --lento
~/godot/godot --path client            # editor; F5 arranca la pantalla de conexión (no probado en este servidor headless)
```
