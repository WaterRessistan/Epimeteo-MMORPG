# STATUS — Epimeteo MMORPG

**Última actualización:** 2026-08-03 · **Fase actual:** 2 en curso (persistencia y autenticación)

## Estado

| Área | Estado |
|---|---|
| Diseño y arquitectura | ✅ Cerrado (`docs/00`, `01`, `02`, `03`) |
| Repositorio git | ✅ Fases 0 y 1 commiteadas; Fase 2 (en curso) commiteada en este push |
| Solución .NET | ✅ `Epimeteo.sln` (Shared + Server + Server.Tests + Shared.Tests + tools) |
| Protocolo | ✅ Envelope, opcodes, tabla de estados, códec MessagePack; + `Login`/`Register`/`AuthResult` tipados |
| Servidor | ✅ Kestrel 5100/5101, sesiones, rate limit, tick 20 Hz, `/status`; + migraciones DbUp al arrancar, `AuthService` (Login/Register) en el hilo de red |
| Cliente Godot | ✅ Conecta, handshake, Ping 1 Hz, RTT; + pantallas `Login`/`Register`, transición desde `Connect` |
| Tests | ✅ 16/16 compartidos + 6/13 servidor en verde, **7 saltados** (necesitan Postgres real, no configurado en esta sesión) |
| Base de datos | 🟡 Esquema y migración `0001_init.sql` escritos; **Postgres NO instalado/verificado en este entorno** — nunca se ha ejecutado la migración de verdad |
| Contenido (`content/`) | ❌ |
| Despliegue | ❌ |

## Entorno

- Desarrollo: WSL2 Ubuntu 24.04 en `/home/mariox/gits/mmorpg`. WSLg disponible (`DISPLAY=:0`).
- **.NET SDK 8.0.423** en `~/.dotnet` (script oficial, sin sudo). `PATH` y `DOTNET_ROOT` en `~/.bashrc`.
  `sudo` no funciona desde la sesión de Claude Code por falta de TTY; si se quiere el paquete de
  apt, hay que lanzarlo desde una terminal normal.
- **Godot 4.5.1 .NET** en `~/godot/`, con enlace `~/godot/godot`.
- Producción: servidor Ubuntu propio. 80/443/8080 ocupados.
  **Sin confirmar qué proceso tiene el 443.** Comprobar en Fase 5 con
  `sudo ss -lntp | grep -E ':(80|443|8080)\b'`.

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

## Hecho en la Fase 2 (hasta ahora, sin verificar contra Postgres real)

Detalle completo del diseño en `docs/fases/FASE-02-persistencia.md`. Código escrito y compila,
`dotnet build`/`dotnet test` en verde, pero **nunca se ha ejecutado contra una base de datos
real** porque esta sesión no tiene `sudo` con TTY para instalar PostgreSQL (ver bloque
"Entorno" más abajo).

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
  `PostgresFactAttribute` — se **saltan** si `ConnectionStrings:Epimeteo` no está configurada;
  en esta sesión se han saltado siempre porque no hay Postgres instalado.
- `appsettings.Development.json.example` con plantilla de cadena de conexión (el real,
  `appsettings.Development.json`, sigue en `.gitignore` y no se ha creado todavía).

### Lo que falta para cerrar la Fase 2 (criterio de aceptación en `FASE-02-persistencia.md §12`)

1. **Instalar PostgreSQL 16 en local** (`sudo apt install postgresql-16`, crear rol y BD
   `epimeteo`) — requiere una terminal con TTY del usuario, no esta sesión. Comandos exactos en
   `docs/fases/FASE-02-persistencia.md §2`.
2. Crear `server/Epimeteo.Server/appsettings.Development.json` (a partir del `.example`) con la
   contraseña real.
3. Arrancar el servidor y comprobar en el log que `MigrationRunner` aplica `0001_init.sql` sin
   errores.
4. Correr `dotnet test` de nuevo — los 7 tests que hoy se saltan deben pasar a ejecutarse y
   pasar en verde contra la BD real.
5. Probar el flujo completo desde el cliente Godot: registrar cuenta → cerrar → reabrir → login
   con las mismas credenciales → contraseña incorrecta → `InvalidCredentials` → 6 intentos
   fallidos seguidos → `RateLimited` en el 6º.
6. Sólo entonces la Fase 2 se da por cerrada y se pasa a la Fase 3 (personajes).

## Siguiente sesión

**Seguir en la Fase 2 — Persistencia y autenticación (Sonnet).** El diseño y el código ya están
escritos (ver arriba); lo único que falta es la parte de infraestructura que necesita `sudo` con
TTY (instalar Postgres) y la verificación end-to-end contra una BD real. Si el servidor de
producción ya tiene Postgres accesible, esta fase podría terminar de verificarse ahí en vez de
en local — a decidir con el usuario al empezar la sesión.

### Comandos útiles

```bash
dotnet build Epimeteo.sln && dotnet test
dotnet run --project server/Epimeteo.Server
dotnet run --project tools/Epimeteo.SmokeClient -- --lento
~/godot/godot --path client            # editor; F5 arranca la pantalla de conexión

# Pendiente de ejecutar (requiere sudo con TTY, ver docs/fases/FASE-02-persistencia.md §2):
sudo apt install -y postgresql-16
sudo systemctl enable --now postgresql
sudo -u postgres createuser -P epimeteo
sudo -u postgres createdb -O epimeteo epimeteo
```
