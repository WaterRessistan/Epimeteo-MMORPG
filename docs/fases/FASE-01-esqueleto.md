# FASE 01 — Esqueleto y primer handshake

**Modelo:** Opus · **Estado:** ✅ completada · **Fecha:** 2026-08-03

> Fase corta en líneas de código, larga en cimientos. Todo lo que se decida aquí (envelope,
> tabla de opcodes, máquina de estados, bucle de tick, colas entre hilos) lo heredan las 14 fases
> siguientes. Ver `docs/01-protocolo.md` para el catálogo completo de mensajes.

## 1. Objetivo

Arrancar el servidor, abrir el cliente Godot, ver **"conectado, RTT 3 ms"** en pantalla.
Sin BD, sin login, sin mundo, sin assets.

## 2. Entregables

| # | Entregable | Fichero(s) |
|---|---|---|
| 1 | Toolchain instalado | .NET 8 SDK (apt) + Godot 4.5.1 .NET en `~/godot` |
| 2 | Raíz del repo | `.gitignore`, `.editorconfig`, `Directory.Build.props`, `Epimeteo.sln` |
| 3 | Protocolo | `shared/Epimeteo.Shared/Net/*` |
| 4 | Servidor | `server/Epimeteo.Server/*` |
| 5 | Cliente | `client/` (proyecto Godot + `scripts/Net/*`) |
| 6 | Tests | `tests/Epimeteo.Shared.Tests/*` |

## 3. Decisiones de esta fase

### 3.1 Envelope y códec
```
frame = [ uint16 opcode LE ][ payload MessagePack ]
```
- `FrameCodec.Encode<T>(Opcode, T)` → `byte[]`; `FrameCodec.TryDecode(ReadOnlySpan<byte>, out header)`.
- Frame vacío o de 1 byte → error de protocolo. Límite duro **16 KB** entrante.
- MessagePack con `[MessagePackObject]` + `[Key(n)]` explícitas y resolver estándar
  (`ContractlessStandardResolver` **no** se usa; queremos claves numéricas fijas).
- `ProtocolVersion.Current = 1`. Se sube a mano al cambiar la forma de un mensaje existente.

### 3.2 Mensajes implementados en la Fase 1
Sólo los cuatro del handshake + kick. El resto del catálogo se añade en su fase.

| Op | Tipo | Estados legales |
|---|---|---|
| `0x0001` | `C2SHello { ProtocolVersion, ClientBuild }` | `Connecting` |
| `0x0004` | `C2SPing { ClientTimeMs }` | cualquiera |
| `0x8001` | `S2CHelloAck { ServerProtocolVersion, TickRate, SnapshotRate, ServerTimeMs }` | — |
| `0x8004` | `S2CPong { ClientTimeMs, ServerTimeMs }` | — |
| `0x8005` | `S2CKick { Reason, DetailCode }` | — |

`S2CKick` no lleva texto: `KickReason` (enum) + `ResultCode` opcional. El cliente traduce.

### 3.3 Máquina de estados
`SessionState { Connecting, Greeted, Authenticated, Loading, InWorld, Closing }`.
Tabla estática `OpcodeTable`: opcode → `{ familia, estados legales }`.
Mensaje en estado ilegal, opcode desconocido o payload ilegible → log + `Kick` + cierre.
**Sin excepciones a esta regla**, ni siquiera para depurar.

### 3.4 Hilos y colas
- **Hilo de red (async, Kestrel):** acepta WS, lee frames, valida tamaño/opcode/estado/rate limit.
- **Mensajes de sesión** (`Hello`, `Ping`) se resuelven **en el hilo de red**: no tocan estado de
  mundo y así el RTT no arrastra hasta 50 ms de espera de tick.
- **Mensajes de mundo** (a partir de la Fase 4) van a `ConcurrentQueue` → se drenan al inicio del
  tick. Se deja el punto de enganche listo (`IWorldInbox`), sin implementación de mundo.
- **Salida:** `Channel<byte[]>` por sesión; un único escritor async por conexión
  (`WebSocket.SendAsync` no admite envíos concurrentes).

### 3.5 Bucle de tick
- 20 Hz, hilo dedicado (`Thread`, no `Task`), `IsBackground = false`.
- Reloj monotónico: `ServerClock.NowMs` sobre `Stopwatch`, **nunca** `DateTime.Now`.
- Compensación de deriva: se acumula el instante objetivo del siguiente tick, no `Sleep(50)`.
- Si un tick tarda > 50 ms se registra warning y **se descarta** el retraso acumulado
  (no se hace catch-up: en un MMO acelerar la simulación es peor que perder un tick).
- Métricas por tick: `last`, `avg`, `p99`, `maxMs` de la última ventana de 100 ticks →
  expuestas en `/status` y logeadas cada 30 s.

### 3.6 Puertos y endpoints
Un solo host Kestrel escuchando en dos endpoints de loopback; el middleware **rechaza** cualquier
ruta que no corresponda al puerto por el que entró (`Connection.LocalPort`).

- `127.0.0.1:5100` → `GET /ws` (upgrade WebSocket). Cualquier otra ruta: 404.
- `127.0.0.1:5101` → `GET /version`, `GET /status`. `/ws` aquí: 404.
- `/status` en Fase 1 es público (loopback). Se autentica en la Fase 13.

### 3.7 Rate limit y timeouts (mínimos de esta fase)
- Token bucket por sesión y familia de opcode; en Fase 1 sólo `Session` (Hello/Ping): 10 msg/s,
  ráfaga 20. El resto de familias queda declarado en la tabla con sus límites de `docs/01 §Rate limiting`.
- Timeout de inactividad: **30 s** sin frame entrante → `Kick(Timeout)`.
- `Hello` debe llegar en los primeros **5 s** de la conexión → si no, cierre.

## 4. Estructura resultante

```
Epimeteo.sln
Directory.Build.props            # LangVersion 12, nullable, TreatWarningsAsErrors (shared+server)
shared/Epimeteo.Shared/
  Net/Opcode.cs  OpcodeFamily.cs  OpcodeTable.cs  SessionState.cs
  Net/FrameCodec.cs  ProtocolVersion.cs  ResultCode.cs  KickReason.cs
  Net/Messages/C2SHello.cs  C2SPing.cs  S2CHelloAck.cs  S2CPong.cs  S2CKick.cs
  Time/ServerClock.cs
server/Epimeteo.Server/
  Program.cs  ServerOptions.cs
  Net/WebSocketEndpoint.cs  Session.cs  SessionManager.cs  SessionMessageHandler.cs
  Net/RateLimiter.cs  TokenBucket.cs
  World/GameLoop.cs  TickMetrics.cs  IWorldInbox.cs
client/
  project.godot  Epimeteo.Client.csproj
  scenes/Connect.tscn
  scripts/Net/NetClient.cs  scripts/Ui/ConnectScreen.cs
tests/Epimeteo.Shared.Tests/
  FrameCodecTests.cs  OpcodeTableTests.cs
```

## 5. Criterio de aceptación

1. `dotnet build Epimeteo.sln` sin warnings.
2. `dotnet test` en verde.
3. `dotnet run --project server/Epimeteo.Server` → log "escuchando en 5100/5101", tick a 20 Hz.
4. `curl 127.0.0.1:5101/status` devuelve JSON con uptime, sesiones y tick time.
5. Godot abre `client/project.godot`, F5 → "Conectado · RTT xx ms" actualizándose a 1 Hz.
6. Cliente con `protocolVersion` falseada → `Kick(VersionMismatch)` y mensaje en pantalla.
7. Enviar un opcode fuera de estado (test manual) → desconexión inmediata.

## 6. Fuera de alcance

BD, login, personajes, mundo, movimiento, assets, TLS, systemd. Cada uno en su fase.

---

## 7. Resultado y desviaciones del plan

Todo lo planeado está hecho y verificado. Cinco cosas salieron distintas del plan y quedan aquí
apuntadas porque afectan a las fases siguientes:

**a) .NET SDK en `~/.dotnet`, no por apt.** `sudo` no funciona sin terminal interactiva desde la
sesión de Claude Code, así que se usó el script oficial (`dotnet-install.sh`, versión **8.0.423**)
con `DOTNET_ROOT` y `PATH` en `~/.bashrc`. Si algún día se prefiere el paquete de apt, basta con
`sudo apt install -y dotnet-sdk-8.0` desde una terminal de verdad y borrar esas líneas.

**b) MessagePack 3.1.8, no 2.5.x.** La 2.5.187 arrastra 11 CVEs conocidos y con
`TreatWarningsAsErrors` el `restore` falla directamente (NU1902/NU1903). La 3.x limpia.
Efecto colateral: el analizador **MsgPack017** prohíbe `{ get; init; } = valor` porque
MessagePack pisa el inicializador con `default` cuando el campo no viene en el frame. Los campos
opcionales de tipo referencia se declaran `string?` y se sanean en el servidor. **Regla para las
fases siguientes: en los mensajes de red, nada de inicializadores en propiedades `init`.**

**c) Dos soluciones.** `Epimeteo.sln` (Shared + Server + Tests + tools) y
`client/Epimeteo.Client.sln` (Client + Shared). Godot necesita su `.sln` junto a `project.godot`
y con configuraciones `ExportDebug`/`ExportRelease`; separarlas evita además que un
`dotnet build` de servidor arrastre el SDK de Godot.

**d) `Serilog.ILogger` con alias.** El SDK Web mete `Microsoft.Extensions.Logging` en los usings
implícitos y `ILogger` queda ambiguo. Los ficheros del servidor llevan
`using ILogger = Serilog.ILogger;`.

**e) `tools/Epimeteo.SmokeClient`, no planeado.** Cliente de consola que verifica el handshake y
las reglas de protocolo sin abrir Godot. Automatiza los criterios 5–7 y es la semilla del load
tester de la Fase 14.

```
dotnet run --project tools/Epimeteo.SmokeClient -- [--lento] [ws://host/ws]
```

### Verificación ejecutada

| Criterio | Resultado |
|---|---|
| `dotnet build Epimeteo.sln` | 0 warnings, 0 errores |
| `dotnet test` | 16/16 |
| Tick a 20 Hz | 312 ticks en 15,5 s · avg 6 µs · p99 52 µs · 0 overruns |
| `/version`, `/status` | JSON correcto |
| Aislamiento de puertos | `5101/ws` → 404, `5100/status` → 404 |
| Handshake + Ping | RTT 0–6 ms en loopback |
| Versión falseada | `Kick(VersionMismatch)` |
| Opcode fuera de estado | `Kick(InvalidState)` |
| Opcode desconocido (0x7FFF) | `Kick(ProtocolError)` |
| Frame de 17 KB | `Kick(ProtocolError)` |
| Frame de texto | `Kick(ProtocolError)` |
| Conexión muda | `Kick(Timeout)` a los 5 009 ms |
| Cliente Godot headless | handshake completo contra el servidor |

### Deuda consciente

- **MessagePack usa generación dinámica de código.** Sirve para el editor y para builds de
  escritorio, pero fallará en un export con AOT. Si en la Fase 15 se exporta con AOT, hay que
  activar el generador de código fuente de MessagePack.
- **`/status` es público** (sólo escucha en loopback). Se autentica en la Fase 13.
- El cliente no reintenta solo: hay que pulsar Enter. Suficiente hasta la Fase 15.
