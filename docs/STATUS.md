# STATUS — Epimeteo MMORPG

**Última actualización:** 2026-08-03 · **Fase actual:** 1 completada → siguiente: **Fase 2**

## Estado

| Área | Estado |
|---|---|
| Diseño y arquitectura | ✅ Cerrado (`docs/00`, `01`, `02`, `03`) |
| Repositorio git | ✅ Fase 0 commiteada; Fase 1 **sin commitear todavía** |
| Solución .NET | ✅ `Epimeteo.sln` (Shared + Server + Tests + tools) |
| Protocolo | ✅ Envelope, opcodes, tabla de estados, códec MessagePack |
| Servidor | ✅ Kestrel 5100/5101, sesiones, rate limit, tick 20 Hz, `/status` |
| Cliente Godot | ✅ Conecta, handshake, Ping 1 Hz, RTT en pantalla |
| Tests | ✅ 16/16 (`FrameCodec`, `OpcodeTable`) |
| Base de datos | ❌ (esquema diseñado, sin migraciones) |
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

## Siguiente sesión

**Fase 2 — Persistencia y autenticación (Sonnet).** Detalle en `docs/03-roadmap-fases.md`.
Empieza levantando PostgreSQL 16 en local y escribiendo `db/migrations/0001_init.sql`.
Los opcodes `Login` (0x0002) y `Register` (0x0003) ya están en la tabla con sus estados legales
y su rate limit; hoy caen en el `default` del despachador y cierran la conexión.

### Comandos útiles

```bash
dotnet build Epimeteo.sln && dotnet test
dotnet run --project server/Epimeteo.Server
dotnet run --project tools/Epimeteo.SmokeClient -- --lento
~/godot/godot --path client            # editor; F5 arranca la pantalla de conexión
```
