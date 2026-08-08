# STATUS — Epimeteo MMORPG

**Última actualización:** 2026-08-07 · **Fase actual:** 9 COMPLETA (combate, monstruos y PvP) →
arranca Fase 10. Desplegado en producción: `wss://epimeteo.waterressistan.duckdns.org/ws`

## Estado

| Área | Estado |
|---|---|
| Diseño y arquitectura | ✅ Cerrado (`docs/00`, `01`, `02`, `03`) |
| Repositorio git | ✅ Fases 0–8 commiteadas; Fase 9 lista para commitear |
| Solución .NET | ✅ `Epimeteo.sln` (Shared + Server + Server.Tests + Shared.Tests + tools) |
| Protocolo | ✅ **Versión 3** (primer cambio desde la Fase 4: `Ping` gana `lastServerTimeMs` para que el servidor mida el RTT él mismo — la compensación de latencia del PvP no puede fiarse de un número del cliente). Todo lo anterior **más combate** (`Attack`, `LootTake`, `CombatEvent`, `EntityDeath`, `LootDrop`, `XpUpdate`, `EntityStats`, `CombatFlagUpdate`) tipado |
| Servidor | ✅ Lo anterior + **combate autoritativo** (`CombatSystem`/`CombatFormulas` puros, RNG determinista con semilla de servidor), monstruos con IA y correa, compensación de latencia con historial de 500 ms y tope de 200 ms, PvP por región validando atacante **y** víctima, flag de combate de 10 s que difiere el logout, `combat_log` |
| Cliente Godot | ⚠️ Todo lo anterior + **tecla de ataque (espacio) con objetivo más cercano y HUD de vida/XP/combate**. Compila sin warnings, pero **no se ha ejecutado nunca**: no hay Godot en este servidor headless |
| Tests | ✅ **137/137 compartidos + 220/220 servidor en verde** (0 saltados) |
| Base de datos | ✅ Postgres 16.14; `0001`–`0005` aplicadas (`0005_combat.sql`: `combat_log`, sólo muertes PvP). Desde la Fase 9 se persisten también `hp`/`mp`/`xp`/`level`, que existían desde la Fase 2 y no se escribían nunca |
| Contenido (`content/`) | ✅ Lo de la Fase 8 + **`content/monsters/{slime,wolf}.json`** y los `spawns[]` de `map.village` en `campo_norte` (nunca en la plaza, que es `no_monsters`) |
| Despliegue | ✅ **En producción**: `epimeteo.service` (systemd, usuario dedicado, `Restart=always`, `ProtectSystem=strict`), nginx + TLS propio en `epimeteo.waterressistan.duckdns.org`, backup diario de Postgres por timer. Fase 9 ya desplegada con `deploy/publish.sh` y verificada contra el servicio real |

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

## Hecho en la Fase 6 — Inventario y equipamiento

Plan completo en `docs/fases/FASE-06-inventario.md`. El diseño de protocolo, esquema de BD y
códigos de error ya estaba cerrado desde las Fases 1–2 (opcodes `0x0030–0x0034`/`0x8030–0x8033`,
`item_instances`, `ResultCode` 300–305, todos reservados sin usar); esta fase fue implementación
sobre ese diseño, sin reabrir el protocolo (`ProtocolVersion` se queda en 2).

- `content/items/*.json`: 7 ítems (`iron_sword`, `wooden_shield` — Weapon, van en la bolsa de
  armas los dos, "arma" en sentido amplio de equipable de combate—, `leather_chest`,
  `copper_ring` — Armor—, `health_potion` — Consumable, cura—, `iron_ore` — Material—,
  `wheat_seed` — Seed, sin lógica de siembra todavía). `content/classes/*.json` gana
  `startingItems`: sin tiendas ni loot, es la única forma de que un personaje nuevo tenga algo
  que mover o equipar.
- `Shared/Data/`: `ItemType`, `EquipCategory`, `EquipSlot`, `ContainerId`, `ItemDefinition`,
  `ItemLoader` (parseo puro, testeable) + `ItemCatalog` (directorio, como `MapCatalog`, pero
  vive en `Shared` porque el cliente también necesita el catálogo completo — un mapa el cliente
  sólo carga el suyo, un inventario puede tener cualquier combinación de ítems a la vez),
  `EquipSlots` (categoría → huecos físicos; `Ring` es el único caso de "uno de varios",
  resuelve a `Ring1` **o** `Ring2`), `InventoryConstants` (capacidades de bolsa, la regla de
  "una arma sólo entra en la bolsa de armas" en una función).
- `Server/Inventory/`: `ItemStack` (mutable, en memoria, sin id de Postgres — no hace falta:
  ver persistencia abajo), `PlayerInventory`, `InventorySystem` (estático y puro dado
  inventario+catálogo, mismo espíritu que `MovementSystem`: mover/apilar/dividir, tirar, usar,
  equipar/desequipar, y `ComputeDerivedStats` para `HpMax`/`MpMax`/stats efectivos — sin
  daño/defensa, eso es Fase 9 y no hay combate contra qué calcularlo).
- **Persistencia: instantánea completa, no un log de diffs.** `InventorySaver` recibe el
  inventario **entero** de un personaje tras cada mutación y hace `DELETE`+`INSERT` en
  transacción — igual que `CharacterPositionSaver` manda el valor actual completo, no un delta.
  Por eso `DropOldest` en la cola es tan seguro para inventario como para posición: perder una
  instantánea vieja no importa si la nueva ya la contiene entera.
- `SessionMessageHandler.CharSelect` carga `item_instances` (containers 0–3) igual que carga la
  fila `characters`; `Zone.Join` ya no hace falta tocarlo — `GameWorld.HandleJoin` manda
  `InventoryFull`+`EquipmentUpdate` tras el join. `GameWorld.DrainMessages` gana los 5 opcodes de
  inventario, resueltos contra `PlayerInventory` en memoria, sin I/O en el tick.
- `S2CSystemMessage` (opcode reservado desde la Fase 1, sin tipar hasta ahora): fallo de
  validación → no hay `InvResult` dedicado (a diferencia de `ShopResult`, un cliente honesto no
  debería intentar un movimiento inválido nunca) → `SystemMessage` con severidad+clave i18n.
- **Cliente Godot:** `Inventory/` nuevo (`InventoryState`, `ItemSlot` con drag&drop nativo de
  Godot + tooltip, `EquipmentPanel`, `InventoryScreen`, overlay con tecla `I`). Sin arte: texto y
  rectángulos, como `WorldRenderer`. `NetClient` gana los 4 eventos y los 5 `Send*`.

### Fallos reales encontrados verificando de punta a punta (no en teoría)

- **Ninguno en el servidor.** El diseño de persistencia (instantánea completa) resultó correcto
  a la primera: verificado con `psql` tras el flujo normal y tras un **reinicio real** del
  servicio de producción con un ítem recién equipado — sobrevivió exacto.
- **Dos en las propias herramientas de verificación**, ambos ya vistos en fases anteriores y
  reconocidos tarde: el flujo de reconexión de `SmokeClient` no esperó a que la cola asíncrona de
  `InventorySaver` drenara antes de leer Postgres de nuevo (mismo problema que ya resolvió
  `WorldBot` para la posición en la Fase 4 — un `Task.Delay(1500)` antes de reconectar). Y al
  añadir el nuevo flujo de inventario al `SmokeClient`, el cupo fijo de "esperar 65 s una vez" ya
  no bastaba para el total de intentos de login de toda la suite: hubo que darle a `SmokeClient`
  el mismo reintento-con-espera-y-keepalive que ya tenía `WorldBot` (`AuthWithRetry`, con `Ping`
  cada 10 s durante la espera para no caer por `IdleTimeoutMs`).

### Verificación Fase 6 (esta sesión, contra el servicio de producción real)

Desplegado con `deploy/publish.sh` (build+test+publish+reinicio, el mismo de la Fase 5) antes de
verificar — el juego en `wss://epimeteo.waterressistan.duckdns.org/ws` ya corre este código.

1. ✅ `dotnet build` sin warnings; `dotnet test`: **105/105 compartidos + 99/99 servidor**
   (`InventorySystemTests` es el grueso: 26 casos puros sobre `PlayerInventory` en memoria).
2. ✅ `SmokeClient` ampliado con el flujo completo de inventario: kit inicial de un guerrero
   recién creado, mover, apilar, equipar (con el `EquipmentUpdate` reflejando `StrEffective`
   subido por el bono del arma), un `Equip` inválido (categoría equivocada) que **no cambia
   nada** y llega `SystemMessage`, usar una poción, tirar la última — y todo sobrevive a
   desconectar y reconectar. 45/45 comprobaciones en verde (incluye lo heredado de las Fases
   1–3).
3. ✅ Reinicio real de `epimeteo.service` con un ítem recién equipado: confirmado en `psql` que
   el estado exacto (espada equipada, escudo en su bolsa) sobrevivió — es la ruta de
   `GameLoopService.StopAsync` → `GameWorld.FlushAllState()` (renombrado de
   `FlushAllPositions`, ahora también vuelca inventario), no sólo el guardado por mutación.

## Hecho en la Fase 7 — Tiendas y armero

Plan completo en `docs/fases/FASE-07-tiendas.md` (incluye una §11 añadida al cerrar, con lo que
pasó de verdad frente a lo planeado). Protocolo, esquema de BD y códigos de error ya estaban
mayormente cerrados desde las Fases 1–2 (`ShopOpen/Buy/Sell/Close`, `ShopData/ShopResult`,
`CurrencyUpdate`, `shop_stock`/`economy_log`) salvo una laguna real: reparar no tenía opcode.

- `db/migrations/0003_shops_economy.sql`: `shop_stock` (stock/precio/restock por tienda+ítem) y
  `economy_log` (append-only, `kind` 1–10 tras esta fase).
- `Shared/Data/`: `ShopDefinition`/`ShopLoader`/`ShopCatalog` (vive en `Shared`, el cliente
  también necesita el catálogo completo, mismo motivo que `ItemCatalog`); `ItemDefinition` gana
  `DurabilityMax`; `ClassDefinition` gana `StartingGold`.
- `content/shops/{general_store,armory}.json`: uno por tienda, NPC dentro, sólo la armería
  repara. `iron_sword`/`leather_chest` ganan `durabilityMax`; las tres clases ganan
  `startingGold: 100`.
- Protocolo: `ShopRepair = 0x0044` (hueco real, no subió `ProtocolVersion` — mismo criterio que
  los opcodes de inventario de la Fase 6: añadir un mensaje nuevo no es cambiar la forma de uno
  que ya existía). 5 mensajes C2S, `ShopSlotInfo`/`S2CShopData`/`S2CShopResult`/
  `S2CCurrencyUpdate` (primera vez tipado, reservado desde la Fase 1).
- `Server/Shop/`: `ShopSystem` (estático y puro, como `InventorySystem`: `TryBuy`/`TrySell`/
  `TryRepair`, nunca toca I/O), `ShopRuntime` (stock en memoria, fusiona lo guardado en Postgres
  con los valores del JSON al arrancar, repone por reloj de pared una vez al segundo).
- `World/NpcEntity.cs`: subtipo de `WorldEntity`, registrado al construir cada `Zone` — sin tocar
  `AoiSystem`/`SnapshotBuilder`, que ya estaban diseñados en la Fase 4 para cualquier tipo de
  entidad. `PlayerEntity` gana `Gold`/`GoldDirty`; el oro viaja en el mismo guardado async que la
  posición (`PositionSave`/`CharacterPositionSaver`), no en uno nuevo.
- `Persistence/Economy/`: `EconomyLogRepository`, `ShopStockRepository`, `EconomySaver` (mismo
  patrón instantánea-fuera-del-tick que posición e inventario).
- **Cliente Godot:** `Shop/ShopScreen.cs` (overlay con pestañas Comprar/Vender/Reparar, reutiliza
  `Inventory.InventoryState` e `ItemSlot` de la Fase 6 tal cual); `WorldScreen` gana la tecla
  `interact` (`E`) que abre la tienda del NPC más cercano o la cierra; `NetClient` gana los 5
  `Send*` de tienda, `ShopDataReceived`/`ShopResultReceived`/`CurrencyUpdateReceived` y `Gold`.

### Tres hallazgos reales, ninguno anticipado en el plan — encontrados por la verificación E2E, no por lectura de código

- **Sin oro inicial.** `characters.gold` se quedaba en su `DEFAULT 0` de BD; ningún personaje
  nuevo podía comprar nada. Añadido `ClassDefinition.StartingGold` (100 en las tres clases),
  insertado por `CharacterRepository.CreateAsync`.
- **Bug real, no sólo de esta fase: el oro guardado no viajaba al entrar al mundo.**
  `WorldJoinRequest` nunca llevaba `Gold`, así que `PlayerEntity.Gold` se quedaba en 0 en cada
  join/reconexión **aunque el personaje tuviera oro guardado de verdad en Postgres** — y el
  siguiente barrido de guardado lo habría sobrescrito con 0. Corregido en `WorldJoinRequest`,
  `SessionMessageHandler` y `Zone.Join`; cubierto con `WorldTests.UnJoin_ConservaElOroGuardado`,
  una regresión que ningún test anterior habría detectado (nada probaba el oro a nivel de
  `GameWorld`, sólo `ShopSystem` puro).
- `EconomyLogKind` (fijado en `docs/02`, valores 1–9, de antes de que existieran las tiendas) no
  contemplaba reparar — se registraba como `Admin` (7), semánticamente incorrecto. Añadido
  `Repair = 10` (columna `smallint` sin `CHECK`, no hizo falta migración).

### Verificación Fase 7 (esta sesión, contra el servicio de producción real)

Desplegada con `deploy/publish.sh` **tres veces** en esta sesión (una por cada hallazgo de
arriba, corregido y re-verificado antes de seguir) antes de dar la fase por cerrada.

1. ✅ `dotnet build` sin warnings; `dotnet test`: **117/117 compartidos + 128/128 servidor**.
2. ✅ `dotnet build client/Epimeteo.Client.csproj`: sin warnings. UI no ejercitada a mano (sigue
   sin haber Godot en este servidor headless).
3. ✅ `tools/Epimeteo.WorldBot --shops-buy` (extendido esta fase con movimiento hacia un punto,
   `WalkTarget`, porque hacía falta acercarse de verdad a un NPC para probar la distancia):
   `ShopOpen` lejos → `TooFarAway`; camina de verdad hasta el NPC; `ShopOpen` cerca → `ShopData`;
   comprar con precio equivocado → `PriceChanged`, oro intacto; comprar de verdad → oro baja
   exactamente lo esperado; vender el escudo del kit inicial → oro sube lo esperado.
   **9/9 comprobaciones en verde.**
4. ✅ Entre medias, `UPDATE item_instances SET durability = 40 ...` por `psql` (nada desgasta
   ítems todavía de verdad) y `tools/Epimeteo.WorldBot --shops-repair <username>`: reconecta con
   el mismo personaje, camina hasta el NPC, repara → durabilidad al máximo, oro baja exactamente
   `(100-40) × 2 = 120`. **5/5 comprobaciones en verde.**
5. ✅ Verificado en `psql`: `gold`, `durability` del ítem reparado, `shop_stock` (con el stock
   bajado por las compras) y `economy_log` (tres filas: `kind` 1/2/10) sobreviven un
   `systemctl restart epimeteo` sin pérdida — mismo criterio que posición e inventario en las
   Fases 4 y 6.

## Hecho en la Fase 8 — Granja y cultivos

Plan completo en `docs/fases/FASE-08-granja-cultivos.md` (incluye una §11 añadida al cerrar, con
lo que pasó de verdad frente a lo planeado). Esquema de BD y los 5 opcodes de granja ya estaban
cerrados desde las Fases 0–1 — a diferencia de las Fases 6–7, **sin ningún hueco real en el
protocolo**. Lo que sí había que decidir de cero era cómo encajaba el job diario (`docs/00 §7`
lo describía como un `UPDATE` SQL directo) en la arquitectura de tick-autoritativo ya fijada.

- **El job diario se calcula en memoria, dentro del tick, no como SQL directo** (D1): un
  `UPDATE` masivo aparte de la cola de guardado por acción del jugador habría sido un segundo
  escritor de `farm_tiles` compitiendo con el primero — condición de carrera real que ningún otro
  sistema del proyecto tiene. `SweepFarmGrowth` corre una vez por segundo (como `SweepRestock` de
  la Fase 7), recupera tantos días de las 05:00 UTC como hayan pasado desde `farm_calendar.
  last_day_index` (uno a uno, así se recuperan días perdidos si el servidor estuvo caído) y
  encola el guardado de cada tile que cambió por la cola de siempre.
- `db/migrations/0004_farm.sql`: `farm_plots`/`farm_tiles` tal cual `docs/02`, más `farm_calendar`
  (nueva, una fila). Parcela comunitaria sembrada por la propia migración en `map.village`
  (origen 6,82, 8×6) — sin propiedad ni compra de parcelas esta fase (`owner_char_id` se queda
  `NULL`, ya anticipado en el esquema).
- `Server/Content/`: `CropDefinition`/`CropLoader`/`CropCatalog` — servidor-only (como
  `ClassCatalog`/`MapCatalog`, no como `ItemCatalog`/`ShopCatalog`): el cliente no necesita el
  catálogo, pinta el `Stage` que ya resuelve el servidor. `content/crops/wheat.json`
  (`growthDaysNeeded: 3`, `season: "Any"` a propósito — FASE-08 §2 D8 — para que la verificación
  E2E no dependa de en qué mes real se ejecute).
- `Shared/Data/`: `FarmTileStatus`, `FarmToolAction`. `ItemDefinition` gana `FarmToolAction?`:
  con un único hueco de herramienta (`EquipSlot.Tool`, reservado desde la Fase 6), sin esto no
  hay forma de exigir "la herramienta correcta" para arar frente a regar. `content/items/
  {hoe,watering_can}.json`, vendidas en `general_store`; `ResultCode.WrongTool = 505` (hueco real
  cerrado, mismo criterio que `ShopRepair` en la Fase 7).
- `Server/Farm/`: `FarmSystem` (puro, como `ShopSystem`), `FarmCalendar` (aritmética de día de
  granja y estación, pura), `FarmRuntime`/`FarmPlotRuntime` (mismo patrón que `ShopRuntime`).
  `Persistence/Farm/`: repositorios Dapper + `FarmTileSaver` (misma cola-descarta-lo-viejo que
  posición/inventario/economía).
- **Cliente Godot:** `NetClient` gana `FarmTileUpdateReceived` y los 4 `Send*`. Sin pantalla de
  granja dedicada esta fase (fuera de alcance explícito, §7 del plan) — el patrón ya está
  probado tres veces (inventario, tienda), no aporta verificación nueva escribirlo ahora sin
  poder ejecutarlo.

### Dos hallazgos reales, ninguno anticipado en el plan — encontrados al escribir la verificación E2E, no por lectura de código

- **Ninguna de las cuatro acciones de granja comprobaba la distancia al tile.** CLAUDE.md §4 es
  explícito y no negociable ("toda petición se valida en servidor contra... distancia") y las
  tiendas ya lo hacían desde la Fase 7 — un descuido real al portar el patrón, no una decisión
  consciente. Corregido con `IsWithinFarmRange` (2 tiles) y `ResultCode.TooFarAway` reutilizado,
  en las cuatro acciones, antes de cerrar la fase.
- **La propia herramienta de verificación tenía la cuenta mal, no el servidor:** el guion regaba
  una sola vez y luego "adelantaba 3 días" esperando ver el trigo maduro (`growthDaysNeeded: 3`).
  Pero regar acelera un día concreto, no todos los que vengan (D1): el progreso real fue
  `1,0 + 0,5 + 0,5 = 2,0`, no `3,0` — el tile se quedó `Planted`. La lógica del servidor estaba
  bien; la expectativa del test no. Corregido rodando 6 días (el margen del peor caso
  "abandonado" de `docs/00 §7`). De paso confirmó que hace falta reiniciar el servicio para que
  el barrido note un `farm_calendar` movido a mano por SQL — el barrido compara contra lo que ya
  tiene en memoria, no relee Postgres solo; en producción esto nunca hace falta porque el índice
  sólo avanza.

### Verificación Fase 8 (esta sesión, contra el servicio de producción real)

Desplegada con `deploy/publish.sh` antes de verificar.

1. ✅ `dotnet build` sin warnings; `dotnet test`: **117/117 compartidos + 173/173 servidor**
   (+40 sobre los 133 previos: `FarmSystemTests`, `FarmCalendarTests`, `CropCatalogTests` puros,
   `FarmTileRepositoryTests`/`FarmCalendarRepositoryTests` contra Postgres real).
2. ✅ `dotnet build client/Epimeteo.Client.csproj`: sin warnings. UI no ejercitada a mano (sigue
   sin haber Godot en este servidor headless).
3. ✅ `tools/Epimeteo.WorldBot --farm-plant`: comprar azada+regadera+semilla, caminar ~45 tiles
   hasta la parcela, arar, plantar, regar. **8/8 comprobaciones en verde.**
4. ✅ Entre medias, `UPDATE farm_calendar SET last_day_index = last_day_index - 6` + `systemctl
   restart epimeteo` (recuperación real de días perdidos, no simulada) y
   `tools/Epimeteo.WorldBot --farm-harvest <username>`: el tile ya en `Ready` al reconectar,
   cosechar da trigo de verdad y el tile vuelve a `Tilled`. **5/5 comprobaciones en verde.**
5. ✅ Verificado en `psql`: `state`/`crop_key`/`growth_days` del tile cosechado sobreviven un
   `systemctl restart epimeteo` sin pérdida — mismo criterio que posición/inventario/economía en
   las Fases 4/6/7.

## Hecho en la Fase 9 — Combate, monstruos y PvP

Plan completo en `docs/fases/FASE-09-combate-pvp.md` (con una §11 al cierre sobre lo que pasó de
verdad frente a lo planeado). Los opcodes de combate estaban reservados desde la Fase 1, pero
**esta fase sí sube la versión del protocolo**: es la primera desde la Fase 4.

- **`ProtocolVersion` 2 → 3.** `C2SPing` gana `LastServerTimeMs`, el eco del último
  `S2CPong.ServerTimeMs`. Sin él el servidor no puede medir el RTT por sí mismo, y la
  compensación de latencia —que decide a quién alcanza un golpe— habría tenido que fiarse de un
  número calculado por el cliente, justo lo que CLAUDE.md §4 prohíbe. Cambiar la forma de un
  mensaje existente es exactamente el criterio de `docs/01` para subir de versión (las Fases 6–8
  no la subieron porque sólo añadían).
- **La compensación mueve la geometría, nunca el permiso** (§2 D2, la decisión de seguridad de la
  fase, y no estaba escrita en ningún sitio). El rebobinado (500 ms de historial, tope de 200 ms)
  resuelve **sólo** el alcance; los flags de zona se miran siempre contra la posición autoritativa
  actual de los dos. Rebobinar también el permiso habría abierto un exploit nuevo justo al cerrar
  el viejo: matar a alguien que **ya está dentro** de la plaza porque 200 ms atrás no lo estaba.
- `Shared/Simulation/`: `DeterministicRng` (xorshift64*, con semilla de servidor),
  `CombatFormulas` (daño, crítico; puro y con daños **exactos** en los tests gracias al RNG
  determinista) y `LineOfSight` (trazado sobre la colisión: no se pega a través de la muralla).
- `Server/Combat/`: `CombatSystem` (las siete validaciones de D3, puro), `PositionHistory`
  (anillo de rebobinado, server-only), `AggroTable` (amenaza por daño, no un objetivo único),
  `MonsterAi` (FSM `Idle→Patrol→Chase→Attack→Returning` con **correa**, para que nadie arrastre un
  monstruo hasta la plaza) y `MonsterSpawner` (respawn temporizado, sin persistir nada).
- **Los monstruos pasan por la misma validación que los jugadores**: la IA sólo *decide*, y
  `GameWorld` pasa su intención por `CombatSystem`. Un monstruo tampoco pega a través de un muro.
- `LootBagEntity` con derecho de saqueo (exclusivo del que más daño hizo durante 30 s) y el
  opcode **`LootTake = 0x0062`**, hueco real del catálogo cerrado: había `LootDrop` (S2C) y
  `ContainerId.LootBag`, pero ningún C2S para coger nada — `InvMove` no vale, opera entre
  contenedores del propio personaje. Mismo criterio que `ShopRepair` en la Fase 7.
- **Flag de combate de 10 s** (`docs/00 §6.2`): salir estando marcado **no** saca del mundo; la
  entidad se queda viva y atacable hasta que expire. Sin esto, "me van a matar" se resuelve con
  Alt+F4.
- **`PositionSave` pasa a `CharacterSave`** y gana vida, maná, XP y nivel: las columnas existen
  desde la Fase 2, se leían en `CharSelect` y **no las escribía nadie**. Daba igual mientras nada
  cambiara la vida; con combate, un moribundo se curaría del todo reconectando.
- `content/monsters/{slime,wolf}.json` y `spawns[]` en `map.village`, siempre en `campo_norte` y
  nunca en la plaza. Los spawns **no** entran en el hash del mapa: el cliente no los necesita.

### Hallazgos de la verificación E2E

Los tres fueron de la **herramienta o de la expectativa**, no del servidor — que es una señal
razonable después de tres fases seguidas encontrando huecos reales en el código de producción:

- El bot no limpiaba `KnownEntities` con `EntityDespawn`, así que "pega al monstruo más cercano"
  acababa apuntando a un cadáver. Lo mismo con los sacos de loot, y ahí el rechazo **correcto**
  por derechos de saqueo ajenos parecía un fallo del loot.
- La penalización de XP (5 %) se trunca a 0 con la XP de un monstruo (0,4 → 0). No es un fallo
  —protege a quien empieza— pero la expectativa del guion sí lo era.
- Entre `campo_norte` (hasta `y = 48`) y `pueblo` (desde `y = 49`) hay una banda sin región
  declarada. No se puede atacar ahí, que es el fallo cerrado que se quería (D3), pero conviene
  saber que existe: comprobar zonas por coordenada en vez de preguntarle al mapa se rompe justo
  ahí.

### Verificación Fase 9 (esta sesión, contra el servicio de producción real)

Desplegada con `deploy/publish.sh` antes de verificar.

1. ✅ `dotnet build` sin warnings; `dotnet test`: **137/137 compartidos + 220/220 servidor**.
2. ✅ `dotnet build client/Epimeteo.Client.csproj`: sin warnings. UI no ejercitada a mano.
3. ✅ `tools/Epimeteo.WorldBot --pvp`: **24/24 comprobaciones en verde** — golpe legal en
   `campo_norte`, muerte con reaparición en el pueblo, **rechazo atacando desde el borde de la
   plaza** (`combat.SafeZone`), rechazo con la víctima refugiada y con los dos dentro, monstruos
   que aparecen solos y mueren dando XP y botín recogible, y **desconectar en combate no saca del
   mundo**.
4. ✅ En `psql`: `combat_log` con **una fila por muerte PvP y ninguna por muerte de monstruo**;
   `characters.hp/xp` sobreviven un `systemctl restart epimeteo`; los monstruos vuelven a aparecer
   solos tras el reinicio. Tick medio de **9 µs** con 6 monstruos y 0 overruns.

**Límite honesto:** el caso exacto del criterio de aceptación —los dos **en alcance** y separados
por la frontera— lo fija el test unitario `DesdeElBordeDeLaPlaza_NoSePuedeAtacarAlDeFuera`, que
afirma primero que están en alcance y luego que se rechaza por zona. El `--pvp` prueba el camino
entero contra el servidor real pero no fija la distancia, porque el margen de llegada del
caminante del bot es de 0,3 tiles. Los dos juntos cubren el criterio; ninguno solo lo haría.

## Hecho en la Fase 10 — Progresión

Plan completo en `docs/fases/FASE-10-progresion.md` (con una §11 al cierre sobre lo que pasó de
verdad frente a lo planeado). El protocolo no sube de versión: sólo faltaba un hueco real
(`AllocateStatPoint = 0x0063`), cerrado con el mismo criterio que `ShopRepair`/`LootTake`.

- `Shared/Simulation/LevelingFormulas.cs`: la curva de XP, pura y exacta (100 × nivel).
- `Shared/Data/`: `ProgressionConstants` (3 puntos de stat por nivel), `StatKind`, y
  `SkillDefinition`/`SkillLoader`/`SkillCatalog` — las habilidades viven en `Shared`, no en
  `Server/Content`, porque la barra del cliente necesita conocerlas (a diferencia de
  `MonsterDefinition`).
- `Server/Combat/LevelingSystem.cs` (puro): concede XP con bucle de niveles de más, sube puntos
  de stat, recalcula HP/MP máximos y cura del todo — nunca deja a nadie peor de lo que estaba.
- `Server/Combat/SkillSystem.cs` (puro): valida nivel → maná → cooldown → alcance/zona, mismo
  reparto Shared/Server que `CombatSystem`. Cooldown de habilidad aparte del de ataque básico
  (`PlayerEntity.SkillCooldowns`, un diccionario, no un único valor).
- Daño/curación de habilidad reutilizan `CombatFormulas.Hit` con un bonus plano (`Power`); curar
  no tira dados — depende del contenido, no de la suerte.
- `ClassDefinition` gana `HpPerLevel`/`MpPerLevel`; HP/MP máximos ahora escalan con el nivel.
- Hueco real cerrado de paso: `stat_str/int/vit/dex` y `stat_points` existían desde la Fase 2,
  se leían y **nadie los escribía** — mismo patrón que el hp/mp/xp/level de la Fase 9.
- `content/skills/*.json`: 3 por clase (guerrero, mago, híbrido — el híbrido con la única
  curación, apuntada siempre a uno mismo).
- Cliente: barra de habilidades en el HUD (teclas 1-3, cooldown visual optimista) y un panel de
  reparto de stats nuevo (tecla `K`), ambos sin arte.

### Dos fallos reales de servidor, encontrados sólo por la verificación E2E

- `AllocateStatPoint` usa `OpcodeFamily.Character` a propósito (D5), pero
  `SessionMessageHandler.IsWorldFamily` nunca incluyó esa familia — el mensaje caía en "opcode no
  implementado" y **expulsaba la sesión** en el primer intento de repartir un punto de stat.
- Subir de nivel concedía puntos de stat de verdad en el servidor, pero el cliente nunca se
  enteraba (`StatPoints` sólo viaja en `EquipmentUpdate`, que sólo se mandaba al equipar). Un
  personaje podía tener 3 puntos reales y seguir viendo los de antes hasta el siguiente cambio de
  equipo.

Ninguno de los dos estaba anotado a propósito como hueco: los sacó a la luz sólo la verificación
contra el servidor real, porque nada en `Server.Tests` ejercita el enrutado de
`SessionMessageHandler` ni el camino completo de red de un `GrantXp`. Detalle completo, con los
hallazgos de herramienta (posiciones de monstruo obsoletas en el bot, persiguiendo un imposible al
otro lado de un muro interno del campo, y quedarse quieto durante el cooldown de una habilidad
casi lo mata) en `docs/fases/FASE-10-progresion.md` §11.

### Verificación Fase 10 (esta sesión, contra el servicio de producción real)

Desplegada con `deploy/publish.sh` antes de verificar (dos veces: una para el fix de
`AllocateStatPoint`/`GrantXp`, otra ya limpia).

1. ✅ `dotnet build` sin warnings; `dotnet test`: **155/155 compartidos + 245/245 servidor**.
2. ✅ `dotnet build client/Epimeteo.Client.csproj`: sin warnings. UI no ejercitada a mano.
3. ✅ `tools/Epimeteo.WorldBot --progression-grind`: **23/23 comprobaciones en verde** — habilidad
   bloqueada por nivel rechazada, Golpe Poderoso hace daño de verdad con cooldown propio (no
   comparte con el ataque básico) y maná real (tercer lanzamiento seguido → `NotEnoughMana`), sube
   de nivel matando monstruos sin tocar SQL, HP/MP máximos escalan exacto con `HpPerLevel`/
   `MpPerLevel`, concede los 3 puntos de la subida y repartirlos sube VIT de 6 a 9 uno a uno.
4. ✅ `tools/Epimeteo.WorldBot --progression-verify`, tras `systemctl restart epimeteo` real:
   **7/7 comprobaciones en verde** — nivel, MP máximo y lleno, HP con un valor sano, puntos de
   stat gastados y VIT sobreviven el reinicio.

**Límite honesto:** "cura del todo al subir" lo fija exacto `LevelingSystemTests` con estado
controlado; el E2E prueba el camino entero contra el servidor real, pero el campo sigue lleno de
monstruos mientras se reparten los puntos después de subir, así que se comprueba con un margen del
90 % en vez de la igualdad exacta. Los dos juntos cubren el criterio; ninguno solo lo haría.

## Hecho en la Fase 11 — Chat y social

Plan completo en `docs/fases/FASE-11-chat-social.md` (con una §11 al cierre sobre lo que pasó de
verdad frente a lo planeado). El protocolo no sube de versión: `ChatSend`/`ChatMessage`
(`0x0070`/`0x8070`) estaban reservados desde la Fase 1, sin tipar hasta ahora — mismo caso que
`SkillCast` en las Fases 9-10. Sin opcodes nuevos: los comandos de barra (`/w`, `/who`, `/help`,
y los de admin) viajan como texto normal dentro de `ChatSend.Text`, reconocidos por el prefijo `/`.

- `Server/Chat/ChatCommandParser.cs` (puro): reconoce el comando y sus argumentos, sin tocar el
  mundo — lo ejecuta `GameWorld`, mismo reparto Shared/Server-puro que `CombatSystem`/`SkillSystem`.
- `Server/Chat/ChatFilter.cs` (puro): censura básica, a propósito simple — una lista fija, no un
  servicio de moderación. `chat_log` guarda el texto sin censurar (es para moderación); lo que se
  retransmite sí va censurado.
- Susurro (`/w`) resuelto buscando por nombre en **todas** las zonas, no sólo la de quien pregunta
  (`GameWorld` está escrito para varias desde la Fase 4, aunque hoy sólo haya una).
- `accounts.is_admin` (hueco real de esquema, migración `0006`): ninguna cuenta se autopromociona,
  se concede a mano por SQL. Se lee una sola vez en `AuthService.LoginAsync` y viaja
  `Session.IsAdmin` → `WorldJoinRequest.IsAdmin` → `PlayerEntity.IsAdmin`, mismo camino que
  `StatPoints`/`Gold` desde `CharSelect`.
- Los cuatro comandos de admin (`/kick`, `/ban`, `/teleport`, `/give`) exigen el objetivo
  conectado —sin excepción para `/ban`, por consistencia con los otros tres, que no tienen forma
  de no exigirlo— y quedan **todos auditados** en `admin_action_log` (tabla nueva, hueco real:
  `docs/02` no la había diseñado). `/ban` hace además el `UPDATE accounts` real dentro del mismo
  `AdminActionSaver` que escribe la auditoría — no hace falta un sink aparte para esa mutación.
- `/teleport` mueve al admin junto al objetivo, no al revés (convención habitual de herramientas
  de GM: "llévame a quien tengo que mirar").
- Cliente: caja de chat en el HUD (`Enter` abre/manda, `T` alterna global/zona), sin arte.

### Dos hallazgos reales de la verificación E2E, ninguno del servidor

- **`Bot.CharacterName` no era el nombre de verdad al reconectar.** Sólo lo era si el bot creaba
  el personaje; Fase 11 es la primera en reconectar un bot y usarlo luego como *objetivo con
  nombre* (`--chat-verify` reutiliza las cuentas de `--chat-setup`). `/give`/`/kick` buscaban a
  alguien que no existía, y `/teleport` "funcionaba" por pura coincidencia (nadie se había movido
  del punto de aparición). Arreglado con `Bot.Name`, tomado de `CharList` al reconectar.
- **El cupo de login por IP (Fase 2) y el timeout de sesión inactiva (Fase 1) chocan por primera
  vez.** Ninguna fase anterior conectaba tantos bots de golpe; si el cupo obliga a uno a esperar
  65 s, los que ya habían conectado no mandan nada mientras tanto y el barrido de 30 s los
  expulsa antes de que termine el rezagado. No es un fallo de ninguno de los dos límites — se
  resolvió reduciendo `--chat-verify` a una sola cuenta nueva y dando margen entre corridas.

Detalle completo en `docs/fases/FASE-11-chat-social.md` §11.

### Verificación Fase 11 (esta sesión, contra el servicio de producción real)

1. ✅ `dotnet build` sin warnings; `dotnet test`: **155/155 compartidos + 272/272 servidor**.
2. ✅ `dotnet build client/Epimeteo.Client.csproj`: sin warnings. UI no ejercitada a mano.
3. ✅ `tools/Epimeteo.WorldBot --chat-setup`: **11/11 comprobaciones en verde** — global y zona le
   llegan a los demás, susurro sólo al destinatario (con eco, sin fuga a un tercero), nombre
   inexistente rechazado, `/who`/`/help` responden, comando de admin sin serlo se rechaza.
4. ✅ `tools/Epimeteo.WorldBot --chat-verify`, tras promocionar la cuenta por SQL: **5/5
   comprobaciones en verde** — `/teleport` mueve al admin, `/give` mete el ítem de verdad
   (apilado sobre el del kit inicial), `/kick` y `/ban` expulsan, y la cuenta baneada no puede
   volver a entrar.
5. ✅ En `psql`: las cuatro acciones de admin con fila en `admin_action_log` (admin, objetivo,
   motivo y detalles correctos); `chat_log` con las líneas de la fase 1; `accounts.status`/
   `banned_until`/`ban_reason` reales para la cuenta baneada.

## Hecho en la Fase 12 — Pipeline de contenido y mapas

Plan completo en `docs/fases/FASE-12-contenido-mapas.md` (con una §11 al cierre sobre lo que pasó
de verdad). **El alcance de esta fase se recortó a propósito, confirmado con el usuario al
empezar:** el roadmap pide "integración real de los packs CC0", pero CLAUDE.md §5 prohíbe
explícitamente generar o descargar assets sin que se pida — así que esta fase construyó toda la
infraestructura de contenido y mapas, y **ningún asset real ni placeholder generado**.
`client/assets/ATTRIBUTIONS.md` queda como plantilla vacía hasta que alguien traiga packs de
verdad.

- `AtlasRegion`/`AtlasRegistryLoader`/`AtlasRegistry` (`Shared/Data`, puros y testeados):
  `client/assets/atlas_registry.json` — vacío por ahora — mapea una clave a una región de una
  imagen. Las entidades se buscan por su propio `defKey` (jugador, monstruo, NPC ya son 1:1 con
  su aspecto, no hace falta un campo nuevo); los ítems ganan `visualKey` en `ItemDefinition`,
  opcional y con el propio `key` como valor por defecto — para cuando convenga que dos variantes
  compartan un mismo sprite provisional.
- `WorldRenderer` consulta el registro antes de dibujar el rectángulo de siempre y cae a él si no
  hay entrada o el fichero no existe en disco — hoy, siempre: cero sprites reales, cero rutas
  hardcodeadas en la lógica (CLAUDE.md §5).
- `tools/Epimeteo.ContentValidator` (proyecto nuevo): carga `content/` con los mismos catálogos
  que el servidor real y comprueba las referencias cruzadas que ningún catálogo mira por sí solo
  (kit inicial de una clase, botín de un monstruo, slots de tienda, semilla/cosecha de un cultivo,
  clase de una habilidad, monstruo de un punto de spawn). Código 0 si todo resuelve, 1 y el
  detalle si no.
- `content/maps/map.forest.json` y `map.mountain.json`: dos zonas exteriores nuevas (48×48, una
  entrada segura + una región `pvp` para el resto, un par de puntos de monstruos cada una) —
  `GameWorld` ya crea una `Zone` por cada mapa de `MapCatalog.All` desde la Fase 4, así que cargan
  y se pueblan solas sin tocar el motor.

### Un hallazgo real, en los tests, no en el servidor

Añadir mapas rompió tres tests de `WorldTests.cs` que llevaban en verde desde la Fase 4: buscaban
al jugador de prueba con `world.Zones.First().FindBySession(1)`, asumiendo sin querer que sólo
había una zona — cierto hasta esta fase. Con tres mapas, `.First()` deja de apuntar
necesariamente a `map.village` y el jugador de prueba (que sí entra ahí) podía no estar en la
zona que tocara. No es un fallo de `GameWorld`: es la clase de suposición que sólo se nota cuando
deja de ser cierta, justo lo que esta fase cambiaba a propósito. Arreglado buscando la zona por
`Map.Key`, no por posición. Detalle completo en `docs/fases/FASE-12-contenido-mapas.md` §11.

### Verificación Fase 12 (esta sesión, contra el servicio de producción real)

1. ✅ `dotnet build` sin warnings; `dotnet test`: **163/163 compartidos + 276/276 servidor**.
2. ✅ `dotnet build client/Epimeteo.Client.csproj`: sin warnings. UI no ejercitada a mano.
3. ✅ `tools/Epimeteo.ContentValidator`: 0 problemas contra el `content/` real y contra el
   desplegado en producción; detecta correctamente una referencia rota a propósito (probado y
   revertido).
4. ✅ Desplegado con `deploy/publish.sh`. `/status` en producción: `world.zones` subió de 1 a
   **3**, `world.monsters` a **12** (los 6 de siempre en `map.village` más 3 en cada zona nueva) —
   los `MonsterSpawner` de las zonas nuevas poblaron sus puntos solos.

**Límite honesto:** sin un sistema de transición entre mapas (fuera de alcance a propósito, §1 del
plan), ningún personaje de verdad puede llegar todavía a las zonas nuevas — se comprueba que
existen, cargan y se pueblan, no que se puedan visitar.

## Hecho en la Fase 13 — Observabilidad y anticheat

Plan completo en `docs/fases/FASE-13-observabilidad-anticheat.md` (con una §11 al cierre sobre lo
que pasó de verdad). Tres de las cuatro líneas que pedía `docs/03` **ya estaban hechas** desde
fases anteriores; el plan empieza reconociéndolo para no duplicar trabajo, y §2 D3 explica por qué
dos de los cuatro "detectores" que pedía el roadmap no se implementan: son **imposibles por
diseño**, y escribirlos habría sido código que no puede dispararse dando falsa sensación de
cobertura.

- **Hallazgo de seguridad, el importante:** `/status` respondía **200 en internet**, sin
  autenticar, filtrando jugadores conectados, colas de guardado y tiempos de tick — `nginx`
  proxificaba `location /` entero al 5101 para servir `/version`, y `/status` viajaba de rebote.
  Cerrado en dos capas independientes: token `Bearer` en el servidor (comparado en tiempo
  constante, y **vacío significa 404, no abierto**) y `location = /version` exacto en nginx.
  `/version` sigue abierto porque es lo que un cliente necesita para saber si le toca actualizarse.
- `Observability/`: registro de métricas Prometheus **a mano**, sin dependencia nueva (§2 D1) —
  contadores, gauges que se leen del estado vivo al exponerlos, e histogramas con buckets fijos.
  `/metrics` publica tick, jugadores, entidades, monstruos, mensajes, sesiones, colas y latencia
  de Postgres.
- `Security/AnomalyRecorder`: **lo que faltaba no era detectar, era sumar.** Había ~29 puntos que
  rechazaban una acción, la logueaban y la olvidaban; un cliente honesto falla alguno por latencia,
  uno parcheado falla el mismo cientos de veces por minuto, y nadie miraba esa diferencia. Ahora
  se cuenta por sesión y tipo en una ventana de 60 s, con escalada de dos pasos (aviso → cierre) y
  umbrales distintos por tipo. Puro y determinista: se prueba una ventana de 60 s sin esperarla.
- `AnomalyMapping` traduce rechazo a anomalía en un solo sitio —los cuatro `Send*Failure` por los
  que pasan los 29— distinguiendo **"no puedes"** (quedarse sin maná, zona segura, inventario
  lleno: juego normal, no cuenta) de **"eso no debería haber llegado"**.
- `anomaly_log` (migración `0007`) con el patrón de sink de siempre.

### Verificación Fase 13 (esta sesión, contra el servicio de producción real)

1. ✅ `dotnet build` sin warnings (solución y cliente); `dotnet test`: **163/163 compartidos +
   305/305 servidor**.
2. ✅ Los seis casos de autenticación en loopback, y desde internet: `/status` y `/metrics` → 404,
   `/version` → 200, `/ws` → 400 (el juego sigue vivo). El parche de nginx se aplicó
   quirúrgicamente sobre el fichero instalado, con copia de seguridad y `nginx -t`: copiarle
   encima el del repositorio habría borrado las líneas de TLS que gestiona Certbot.
3. ✅ `tools/Epimeteo.WorldBot --anticheat`: **4/4 en verde** — por debajo del umbral no pasa nada,
   insistir desconecta, y el bot honesto de la misma corrida no lo paga.
4. ✅ En `psql`, `anomaly_log` con exactamente dos filas por corrida: aviso a los 30 y desconexión
   a los 120. En el log, `WRN` y luego `ERR` — que es *la alerta* (§2 D6).
5. ✅ Coste de la instrumentación: tick medio **10 → 11 µs**, p99 **41 → 33 µs**. Dentro del ruido.

**Hallazgo preexistente, anotado y no arreglado:** el arnés de bots se come a veces su propia cola
de salida (`KickReason.InternalError`, 16 veces en dos días, la primera durante la Fase 9). No lo
causa esta fase y el servidor se comporta como debe; arreglar el arnés no es trabajo de una fase de
observabilidad. Sí se arregló **la comprobación**, que pasaba por suerte de temporización: ahora
drena antes de afirmar, y afirma lo preciso (que la escalada no lo alcanzó) en vez de un
`Kicked is null` frágil.

**Límite honesto:** los umbrales son provisionales y generosos, y **no se han validado contra
tráfico real de jugadores** porque todavía no hay ninguno. Están puestos para no producir falsos
positivos, no para atrapar a nadie con eficacia; `anomaly_log` existe para apretarlos con datos
cuando los haya.

## Hecho en la Fase 15 (primera mitad) — Launcher/parcheador

`docs/03` mete en una sola fase tres cosas de naturaleza distinta: launcher/parcheador (servidor +
herramienta de consola), y audio/UX/opciones/builds de distribución (cliente Godot). **Esta
sesión sólo hizo la primera**, decisión tomada con el usuario antes de escribir el plan: este
servidor no tiene Godot instalado y nunca ha tenido entorno gráfico, así que la UI interactiva no
se puede verificar, y el audio choca con la política de assets de CLAUDE.md §5 (nada de generar
o descargar sin que se pida). Plan completo, con una §6 al cierre, en
`docs/fases/FASE-15-pulido-release.md`.

- `client-build/`: directorio nuevo para la salida de una build de Godot (aún no existe ninguna
  real; sólo un `README.md` marcador). No se llama `release/` — ese nombre ya caía en un patrón
  `.gitignore` genérico heredado de la plantilla de .NET (`[Rr]elease/`, para `bin/Release/`) y el
  propio marcador se habría quedado sin versionar sin que nadie se diera cuenta.
- `tools/Epimeteo.ReleaseTool`: genera `client-build/manifest.json` (SHA-256 + tamaño por
  fichero, rutas siempre con `/`).
- `/files/{**path}` en el servidor: público, sin token, en la lista blanca del puerto HTTP junto a
  `/version`. `Files/SafeFileResolver.cs` (con 12 tests) valida la ruta pedida contra path
  traversal en dos capas independientes, aparte del endpoint para poder probarla sin levantar
  Postgres.
- `tools/Epimeteo.Launcher`: descarga lo que falte o haya cambiado (verificando el hash tras
  descargar antes de mover el fichero final), y **borra** del directorio local lo que ya no está
  en el manifiesto — un parcheador que sólo añade dejaría basura de cada build anterior.
- `deploy/nginx-epimeteo.conf`: `location /files/` nueva, parcheada quirúrgicamente sobre el
  fichero instalado (igual que en la Fase 13, nunca sobrescribiendo por las líneas de Certbot).

**Hallazgo real de esta sesión, con impacto:** el primer despliegue **tumbó producción varios
segundos** (bucle de reinicio, WebSocket incluido) porque `deploy/publish.sh` sincronizaba
`content/` a `/opt/epimeteo` pero nunca aprendió a hacer lo mismo con `client-build/`, y el
servidor **lanza a propósito** si no encuentra ese directorio. `dotnet run` en este repositorio no
lo detecta (encuentra `Epimeteo.sln` subiendo y resuelve igual); sólo se ve corriendo el binario
publicado tal cual queda en producción. Arreglado extendiendo `publish.sh` con el mismo patrón de
`rsync` + enlace simbólico que ya tenía `content/`, y verificado con un segundo despliegue que sí
levantó limpio. Detalle completo en FASE-15 §6.

**Verificado contra producción:** el launcher completo (descarga inicial, no-op, reemplazo tras
cambio, borrado de sobrantes) contra `https://epimeteo.waterressistan.duckdns.org` de verdad;
varias formas de traversal (`..` literal → 404; `..%2f`/`%2e%2e` codificados → 400, los rechaza
Kestrel antes incluso de llegar al resolver — capa extra no planeada); `/status` y `/metrics`
siguen en 404 (el hallazgo de la Fase 13 no se reabrió). 163/163 + 317/317 tests.

## Siguiente sesión

**Fase 15 (segunda mitad) — Audio, UX, opciones y builds de distribución · Sonnet · necesita
Godot.** Pendiente hasta que haya una máquina con entorno gráfico: menú de opciones, remapeo de
teclas, pantalla completa, transiciones, audio (una vez haya assets reales o se pida generarlos
explícitamente, CLAUDE.md §5), y las builds de exportación de Windows/Linux con la primera versión
etiquetada — que además necesitan los *export templates* de Godot instalados, que tampoco están.
Cuando exista una build real: copiarla a `client-build/`, `dotnet run --project
tools/Epimeteo.ReleaseTool -- client-build` para el manifiesto, y `deploy/publish.sh` ya la
sincroniza a producción sola.

**Fase 14 — Escalado multi-zona · Opus · _opcional_, sigue sin tocarse.**
`docs/03-roadmap-fases.md` dice explícitamente **"no la hagas por defecto"**: con el tick medio en
**11 µs** sobre un presupuesto de 50 000 µs (Fase 13), no hay nada que escalar todavía.

**Operación, nuevo de esta fase:** `/files/` es público desde internet, como `/version` —
cualquiera puede descargar la build del cliente sin autenticarse, que es lo que tiene que pasar
para que alguien pueda instalar el juego. `/status` y `/metrics` siguen exigiendo
`Epimeteo:MetricsToken` (Fase 13); `Epimeteo:MetricsToken` ya está configurado en
`/opt/epimeteo/app/appsettings.Production.json` (fuera de git, permisos 600). Para consultar
`/status` o `/metrics` desde fuera de la máquina, **túnel SSH al 5101** — nginx no da salida a esas
rutas a propósito. Queda pendiente, si alguien quiere gráficas: montar un Prometheus que haga
*scrape* de `/metrics` y un Grafana encima. El endpoint ya está en el formato estándar; instalarlos
es trabajo de operación, no de código.

**Pendiente aparte, en cuanto haya una máquina con entorno gráfico:** abrir `client/project.godot`
en Godot 4.5 y comprobar a mano lo que este servidor headless no puede: dos clientes viéndose
mover (Fase 4), el drag & drop del inventario (`I`, Fase 6), la tienda (`E`, Fase 7), el combate
(espacio, Fase 9), la barra de habilidades (teclas 1-3) y el panel de stats (`K`, Fase 10), y el
chat (`Enter`, `T`, Fase 11). El cliente puede apuntar a
`wss://epimeteo.waterressistan.duckdns.org/ws` en vez de `127.0.0.1`.

**Pendiente de decidir, no técnico:** los datos de prueba acumulados en la BD (cuentas `Bot*`/
`smoke_*`/`farm_*`/`pvp_*`/`prog_*`/`chat_*` de `SmokeClient`/`WorldBot` a lo largo de las
Fases 1–12, incluidas varias ya baneadas por las pruebas de `/ban`) — si el juego se va a anunciar
de verdad, alguien tiene que decidir si se limpian antes. Esta sesión no ha borrado nada. Y, nueva
de esta fase: **en cuanto alguien traiga packs de arte CC0 reales**, rellenar
`client/assets/ATTRIBUTIONS.md` y las entradas de `client/assets/atlas_registry.json` — la
infraestructura ya está lista, sólo falta el contenido.

**Verificación real que sólo puede hacer un humano:** conectar desde un móvil en datos 4G (no
en la red del servidor) a `wss://epimeteo.waterressistan.duckdns.org/ws` con el cliente Godot, en
cuanto exista una build para probarlo.

**Detalle menor visto de pasada, no de esta fase:** `LoginAttemptRepositoryTests.
CountRecentAsync_NoMezclaIntentosDeOtraIp` (Fase 2) es ocasionalmente flaky —
`UniqueTestIp()` sortea sobre sólo 253 valores (`203.0.113.2-254`) sin excluir los ya usados en
la misma corrida, así que dos tests concurrentes pueden chocar de IP por puro azar. No es un
fallo de esta fase (pasa 100% en aislado); se deja anotado para quien le toque tocar esos tests.

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

# Desplegar a producción y verificar de punta a punta (incluye el flujo de inventario)
bash deploy/publish.sh
dotnet run --project tools/Epimeteo.SmokeClient

# Tiendas (Fase 7): dos corridas con una manipulación manual de durabilidad por psql entre medias
dotnet run --project tools/Epimeteo.WorldBot -- --shops-buy
# UPDATE item_instances SET durability = 40 WHERE owner_char_id = <characterId> AND container = 1 AND slot = 0;
dotnet run --project tools/Epimeteo.WorldBot -- --shops-repair <username>

# Granja (Fase 8): dos corridas, recuperación real de días perdidos vía psql + reinicio entre medias
dotnet run --project tools/Epimeteo.WorldBot -- --farm-plant
# UPDATE farm_calendar SET last_day_index = last_day_index - 6;
# sudo systemctl restart epimeteo
dotnet run --project tools/Epimeteo.WorldBot -- --farm-harvest <username>

# Combate y PvP (Fase 9): dos bots de verdad, 24 comprobaciones. Tarda ~4 min.
dotnet run --project tools/Epimeteo.WorldBot -- --pvp
dotnet run --project tools/Epimeteo.WorldBot -- --pvp --lag-ms 150   # con latencia real de por medio
```
