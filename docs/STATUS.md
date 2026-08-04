# STATUS — Epimeteo MMORPG

**Última actualización:** 2026-08-04 · **Fase actual:** 5 COMPLETA (despliegue mínimo) →
arranca Fase 6. El juego está en producción de verdad: `wss://epimeteo.waterressistan.duckdns.org/ws`

## Estado

| Área | Estado |
|---|---|
| Diseño y arquitectura | ✅ Cerrado (`docs/00`, `01`, `02`, `03`) |
| Repositorio git | ✅ Fases 0–4 commiteadas; Fase 5 lista para commitear |
| Solución .NET | ✅ `Epimeteo.sln` (Shared + Server + Server.Tests + Shared.Tests + tools) |
| Protocolo | ✅ **Versión 2**. Envelope, opcodes, tabla de estados, códec MessagePack; auth, los 5 opcodes de personaje y los de mundo (`InputState`, `EntitySpawn`, `EntityDespawn`, `Snapshot`, `ZoneFlagsUpdate`) tipados |
| Servidor | ✅ Lo anterior + **el tick simula de verdad**: `Zone` con entidades, colas de input, `CellGrid`/`AoiSystem`, `SnapshotBuilder`, `MapCatalog` y guardado de posición fuera del tick |
| Cliente Godot | ⚠️ Conecta, handshake, login/registro, `CharacterSelect` y **`World.tscn`** (predicción, reconciliación, interpolación, cámara, HUD, simulador de latencia). Compila sin warnings, pero **no se ha ejecutado nunca**: no hay Godot en este servidor headless |
| Tests | ✅ **84/84 compartidos + 58/58 servidor en verde** (0 saltados) |
| Base de datos | ✅ Postgres 16.14; `0001_init.sql` + `0002_character_name_format.sql` aplicadas |
| Contenido (`content/`) | ✅ `content/classes/*.json` + `content/maps/map.village.json` (96×96, colisión y regiones) |
| Despliegue | ✅ **En producción**: `epimeteo.service` (systemd, usuario dedicado, `Restart=always`, `ProtectSystem=strict`), nginx + TLS propio en `epimeteo.waterressistan.duckdns.org`, backup diario de Postgres por timer |

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
  como estaba previsto (`CLAUDE.md §2`), sin conflicto.
- **Este servidor de producción es el mismo desde la Fase 2.** No hay una BD "de desarrollo"
  aparte: `epimeteo` (rol y base) es la real, con datos reales acumulados de las pruebas de cada
  fase (cuentas `BotA_*`/`BotB_*` de `SmokeClient` y `WorldBot`, entre otras). La Fase 5 no borró
  nada de eso al pasar a producción — es una decisión pendiente, no tomada por esta sesión, si
  conviene limpiarlo antes de anunciar el juego.

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

## Hecho en la Fase 4 — CERRADA en la parte de servidor

Plan completo en `docs/fases/FASE-04-mundo-movimiento.md`. Los puntos 1–7 de su §12 están hechos y
verificados; el punto 8 (cliente Godot) no, por falta de entorno gráfico — es la salida que el
propio plan contempla en §12 y §13.7.

- `shared/Epimeteo.Shared/Simulation/`: `MovementSystem` (paso fijo de 50 ms, ejes separados para
  deslizar por las paredes), `CollisionMap`, `RegionSet`, `AoiGrid`, `ClientPrediction`,
  `SimulationConstants` y los tipos de valor (`Vec2`, `TilePos`, `Facing`, `ZoneFlags`…). Es el
  código que ejecutan **literalmente los dos lados**.
- `shared/Epimeteo.Shared/Data/`: `MapDefinition` + `MapLoader` (valida al cargar y calcula el
  hash FNV-1a que viaja en `WorldEnter`).
- `content/maps/map.village.json`: 96×96 (6×6 celdas de AOI), muralla, edificio con esquinas,
  puerta de un tile y las regiones `plaza` (segura) y `campo_norte` (PvP).
- `server/Epimeteo.Server/World/`: `Zone`, `WorldEntity`/`PlayerEntity`, `EntityIdAllocator`,
  `InputQueue` (cubo de fichas 20/s + ráfaga 6), `CellGrid`, `AoiSystem`, `SnapshotBuilder`,
  `GameWorld`. El tick pasó de estar vacío a simular en el orden de `docs/00 §4`.
- `Persistence/Characters/CharacterPositionSaver.cs`: cola fuera del tick, guardado escalonado
  cada 30 s por jugador, prioritario al salir, y vaciado al apagar.
- Protocolo **v2**: `C2SInputState`, `S2CEntitySpawn`, `S2CEntityDespawn`, `S2CSnapshot`,
  `S2CZoneFlagsUpdate`; `S2CWorldEnter` gana `MapHash` y `MyEntityId` pasa a ser un id de entidad
  real. Anotado en `docs/01-protocolo.md`.
- `tools/Epimeteo.WorldBot`: N clientes de verdad que hablan el protocolo real y ejecutan la misma
  predicción y reconciliación que ejecutará el cliente Godot. Es lo que sustituye al "abre dos
  Godot y míralos" en un servidor headless.

### Decisiones y hallazgos de esta sesión

- **`dtMs` deja de integrarse.** El input es un comando de paso fijo (`FASE-04 §2 D1`); el reloj
  del cliente ya no entra en la simulación. Con el clamp anterior a `[0,100]` que decía `docs/01`,
  quien mintiera podía ir exactamente al doble de velocidad.
- **Fallo real encontrado y corregido en `Session`** (no era del bot): el bucle de lectura cortaba
  en cuanto había un cierre en marcha. Con un cliente que aún estaba enviando —justo el caso de
  quien es expulsado por inundar de inputs— el servidor dejaba de leer, la conexión se cortaba de
  golpe y el `S2CKick` ya enviado se perdía antes de que el cliente lo leyera: expulsión sin
  motivo visible, y sólo a veces. Ahora se sigue drenando (sin despachar) hasta el frame de cierre
  del cliente o hasta 2 s de gracia. Cubierto por `SessionCloseTests`, que falla sin el arreglo.
- El cupo de 5 intentos de login por minuto **y por IP** hace que una flota de bots de carga no
  quepa: sale toda de la misma IP. `Epimeteo:LoginAttemptsPerMinute` ya es configurable; para las
  corridas de carga se sube en el servidor de pruebas, y el `WorldBot` además espera y reintenta
  (sosteniendo la sesión con `Ping`, que si no cae por `IdleTimeoutMs`).

### Verificación Fase 4 (esta sesión, contra el servidor y Postgres reales)

Criterio de aceptación de `FASE-04 §13`:

1. ✅ `dotnet build` sin warnings; `dotnet test` **68/68 compartidos + 58/58 servidor**, 0 saltados.
2. ✅ `WorldBot` con 2 bots y 0 ms de lag: **24/24**, y repetido 3 veces seguidas para descartar
   que el fallo intermitente del `Kick` siguiera ahí.
3. ✅ Con `--lag-ms 150`: **24/24**. Cero correcciones de reconciliación en 12 s de movimiento
   (el criterio pedía menos de una por segundo) y error máximo 0,000 tiles.
4. ✅ Con `--bots 10`: **28/28**, tick medio **0,09 ms** y 0 overruns en `/status`.
5. ✅ Se mueve, se desconecta y vuelve donde lo dejó (0,00 tiles de desvío), confirmado además en
   `psql`: `characters.pos_x/pos_y` = (56.38, 68.30), distinto del spawn (48.50, 60.50).
6. ✅ `SIGINT` al servidor con 2 jugadores dentro y moviéndose: log `Cola de posiciones cerrada
   tras 16 guardados`, y en la BD las dos posiciones del instante del apagado (56.38 y 59.63),
   no el spawn.
7. ⏳ Godot: el cliente está escrito y compila (`dotnet build client/Epimeteo.Client.csproj`, 0
   warnings), pero **no se ha ejecutado**: este servidor no tiene entorno gráfico. Es lo único
   de la fase que queda por ver funcionando.

## Hecho en la Fase 4 — cliente Godot (punto 8 de §12)

- `client/scripts/World/`: `WorldScreen` (orquesta, compara `MapHash`, reparte los mensajes),
  `LocalPlayer` (acumulador de 50 ms → `ClientPrediction` → `InputState`), `RemoteEntity`,
  `WorldRenderer` (`_Draw`, tiles y rectángulos, Y-sort por `pos.Y`), `WorldCamera`,
  `ClientContent` (localiza `content/` fuera del proyecto), `InputActions`.
- `client/scripts/Ui/WorldHud.cs`: posición, RTT, región con aviso de ZONA HOSTIL y contador de
  correcciones/error máximo. Sin arte, **el HUD es el instrumento de aceptación** de la fase.
- `client/scripts/Net/`: `NetClient` gana los cuatro eventos de mundo y `SendInput`;
  `NetLagSimulator` (`--lag-ms=150` o `EPIMETEO_LAG_MS`) retiene frames en los dos sentidos.
- `client/scenes/World.tscn` nueva; `WorldPlaceholder.tscn` y su script, borrados.
- `client/project.godot`: acciones `move_*` con WASD y flechas. Los códigos de tecla se
  comprobaron contra `GodotSharp 4.5.1` por reflexión, no de memoria: un keycode mal puesto no da
  error de compilación, simplemente el personaje no anda.

### Desviaciones respecto al plan de la fase (§7), a propósito

- **No existen `PredictionBuffer.cs` ni `Reconciler.cs`.** Esa lógica ya estaba en
  `Shared/Simulation/ClientPrediction.cs`, que es lo que ejecuta el `WorldBot`. Duplicarla en el
  proyecto de Godot habría significado verificar una copia y jugar con otra.
- **La interpolación y su reloj se movieron a `Shared`** (`EntityInterpolator`,
  `InterpolationClock`) por el mismo motivo, y ahí sí tienen tests (16 nuevos). Eran la única
  pieza de netcode que quedaba dentro del proyecto de Godot, es decir, la única que no se podía
  comprobar en este servidor. Ahora en `client/scripts/World/RemoteEntity.cs` sólo queda la
  traducción entre los mensajes de red y esa pieza.

### Fallo corregido al escribir el cliente

El acumulador de pasos vaciaba todo el tiempo pendiente de golpe. Tras un alt-tab o un parón del
sistema eso son decenas de `InputState` en un frame, y el presupuesto del servidor (20/s con
ráfaga de 6) lo lee como intento de correr más de la cuenta: **un jugador honesto acababa
expulsado por minimizar el juego**. Ahora se dan como mucho 2 pasos por frame y el resto del
desfase se descarta, que es la misma decisión que tomó el servidor en la Fase 1 con su bucle.

## Hecho en la Fase 5 — Despliegue mínimo

Plan completo en `docs/fases/FASE-05-despliegue.md`, escrito y ejecutado en esta sesión tras
diagnosticar el servidor real (`docs/00 §5` ya no tiene el aviso "pendiente de confirmar").

- **Hallazgo antes de tocar nada:** nginx ya tenía el 443, sirviendo `waterressistan.duckdns.org`
  (otro dominio, ya en producción). DuckDNS resultó ser **wildcard** (`dig` confirma que
  cualquier subdominio resuelve a la misma IP), así que no hizo falta DNS nuevo:
  `epimeteo.waterressistan.duckdns.org` ya apuntaba aquí.
- `deploy/nginx-epimeteo.conf`: fichero **propio**, nunca se tocó
  `/etc/nginx/sites-available/default` (gestionado por Certbot, dominio ajeno ya en producción).
  Certificado propio con `certbot --nginx -d epimeteo.waterressistan.duckdns.org` — no reutiliza
  ni modifica el otro certificado.
- **Fallo real encontrado:** `Program.cs` leía `Connection.RemoteIpAddress` directamente para el
  rate limit de login (5/min por IP, Fase 2). Detrás de nginx eso habría sido siempre
  `127.0.0.1` — el límite habría dejado de proteger a nadie, todo el mundo habría compartido el
  mismo cupo. Arreglado con `ForwardedHeaders` (`KnownProxies` restringido al loopback) y
  verificado a mano: una conexión con `X-Forwarded-For: 203.0.113.77` falsificado quedó
  registrada con esa IP en los logs, tal y como se espera que ocurra detrás de nginx.
- `Content/ContentPaths.cs`: ahora prueba primero `content/` junto al binario (lo que deja el
  publish) y sólo si no está, sube buscando `Epimeteo.sln` (dev/test sin cambios). Misma idea que
  `ClientContent` del cliente Godot (Fase 4).
- Usuario de sistema `epimeteo` (sin shell), `/opt/epimeteo/{app,content,backups}`,
  `epimeteo.service` (systemd, `Restart=always`, `ProtectSystem=strict`, `ProtectHome=true`,
  habilitado para arrancar solo). `content/` vive en `/opt/epimeteo/content` y `app/content` es un
  enlace a él (Fase 5, publish.sh).
- `deploy/publish.sh`: build + test + publish (framework-dependent) + rsync + reinicio +
  comprobación de `/version`. **No publica si los tests fallan.**
- `deploy/backup-postgres.sh` + `epimeteo-backup.{service,timer}`: `pg_dump` diario a las 04:30,
  purga a los 14 días, corre como `epimeteo` vía `EnvironmentFile` (`/opt/epimeteo/backup.env`,
  gitignored). Restauración probada en una BD aparte (`epimeteo_restore_test`, borrada después):
  recuentos de filas idénticos al original.

### Dos fallos de mi propio script, encontrados al probarlo de verdad

- `mktemp -d` crea directorios en `700`, y `rsync -a` arrastra ese modo al destino: la primera
  publicación dejó `/opt/epimeteo/app` en `700` por accidente, no por decisión. `chmod 755`
  explícito después del `rsync`.
- Ese mismo `700` accidental hizo que el chequeo `[ -f .../appsettings.Production.json ]` del
  propio script (ejecutado como `ubuntu`, no como `epimeteo`) dijera "falta" aunque el fichero
  **sí estaba** — sólo no podía atravesar el directorio para verlo. Cambiado a `sudo test -f`.
  Sin este arreglo, una reinstalación limpia se habría quedado atascada en su propio primer
  arranque.
- `dotnet publish` copia `appsettings.Development.json` al directorio de salida si existe en el
  árbol de trabajo (tiene la contraseña de desarrollo): se borra explícitamente del directorio de
  publicación antes de sincronizar, no llega ni de paso a `/opt/epimeteo`.
- `rsync --delete` sin excluir `appsettings.Production.json` lo habría borrado en cada publish,
  porque no existe en el origen (es un secreto, no está en git). `--exclude` añadido.

### Verificación Fase 5 (esta sesión, contra el dominio público real)

Criterio de aceptación de `FASE-05 §6`:

1. ✅ `nginx -t` en verde; `epimeteo.service` activo y **habilitado** (sobrevive a un reinicio).
2. ✅ `certbot certificates` lista el certificado propio (caduca 2026-11-02); `certbot renew
   --dry-run` en verde.
3. ✅ `tools/Epimeteo.SmokeClient` contra `wss://epimeteo.waterressistan.duckdns.org/ws` —
   **33/33 comprobaciones en verde**, incluido el flujo completo de personajes. El log del
   servidor confirma que la conexión salió y volvió por la IP pública real del servidor
   (`130.110.232.218`, no `127.0.0.1`): `ForwardedHeaders` funcionando de punta a punta.
   **No probado desde un dispositivo en una red 4G real** — esta sesión no tiene acceso a uno;
   la evidencia indirecta (DNS público, certificado de una CA pública, firewall abierto en 443,
   IP pública en los logs) es lo más cerca que se puede llegar sin él.
4. ✅ `deploy/publish.sh` corrido dos veces seguidas: la segunda no rompió nada.
5. ✅ Restauración de `pg_dump` en `epimeteo_restore_test`: recuentos de filas idénticos al
   original en `accounts`, `characters`, `item_instances`.
6. ✅ `docs/00-arquitectura.md §5` actualizado, ya no dice "pendiente de confirmar".

## Siguiente sesión

**Fase 6 — Inventario y equipamiento · Sonnet.** Siguiente en `docs/03-roadmap-fases.md`.

**Pendiente aparte, en cuanto haya una máquina con entorno gráfico:** abrir `client/project.godot`
en Godot 4.5 y comprobar el punto 7 del criterio de la Fase 4 — dos clientes en el mismo mapa,
viéndose moverse, movimiento propio inmediato y las paredes parando. El HUD da los números
(correcciones y error máximo) sin herramientas. Con `--lag-ms=150` se reproduce el caso jugable.
Es lo único de la Fase 4 que no se ha visto funcionar. Ahora que el juego está en producción, el
cliente puede apuntar a `wss://epimeteo.waterressistan.duckdns.org/ws` en vez de `127.0.0.1`.

**Pendiente de decidir, no técnico:** los datos de prueba acumulados en la BD (cuentas `Bot*` de
`SmokeClient`/`WorldBot` a lo largo de las Fases 1–5) — si el juego se va a anunciar de verdad,
alguien tiene que decidir si se limpian antes. Esta sesión no ha borrado nada.

**Verificación real que sólo puede hacer un humano:** conectar desde un móvil en datos 4G (no
en la red del servidor) a `wss://epimeteo.waterressistan.duckdns.org/ws` con el cliente Godot, en
cuanto exista una build para probarlo.

Recordatorio de entorno: este servidor de producción ya tiene `dotnet`, Postgres y el rol
`epimeteo` listos; no hace falta repetir la instalación en próximas sesiones aquí. La contraseña
de desarrollo vive sólo en `server/Epimeteo.Server/appsettings.Development.json` (gitignored) —
si se pierde, se resetea con `sudo -u postgres psql -c "ALTER ROLE epimeteo WITH PASSWORD '...';"`.

### Comandos útiles

```bash
dotnet build Epimeteo.sln && dotnet test
dotnet run --project server/Epimeteo.Server
dotnet run --project tools/Epimeteo.SmokeClient -- --lento

# El servidor necesita DOTNET_ENVIRONMENT=Development para leer appsettings.Development.json
DOTNET_ENVIRONMENT=Development dotnet run --project server/Epimeteo.Server

# Netcode: 24 comprobaciones con 2 bots, 28 con carga. Requiere el servidor arriba.
dotnet run --project tools/Epimeteo.WorldBot
dotnet run --project tools/Epimeteo.WorldBot -- --lag-ms 150

# Para --bots 10 hay que subirle el cupo de login al servidor de pruebas (todos salen de una IP)
DOTNET_ENVIRONMENT=Development Epimeteo__LoginAttemptsPerMinute=200 dotnet run --project server/Epimeteo.Server
dotnet run --project tools/Epimeteo.WorldBot -- --bots 10

~/godot/godot --path client            # editor; no hay Godot en este servidor headless
```
