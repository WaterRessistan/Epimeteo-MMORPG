# 03 — Roadmap de fases

Una fase = **una sesión de Claude Code**. Al empezar la sesión: "Lee CLAUDE.md y
docs/STATUS.md, vamos con la Fase N". Al terminar: actualizar `docs/STATUS.md` y cerrar.

Cada fase tiene su propio doc de detalle en `docs/fases/FASE-XX-*.md`, que se escribe **al
principio de esa fase**, no ahora (escribirlos todos ahora sería tirar tokens en planes que
cambiarán).

| # | Fase | Modelo | Entregable verificable |
|---|---|---|---|
| 0 | Diseño y arquitectura | Opus | ✅ Este documento, `CLAUDE.md`, protocolo y esquema de BD |
| 1 | Esqueleto y primer handshake | **Opus** | Cliente Godot conecta por WS, `Hello`/`HelloAck`/`Ping`/`Pong`, RTT en pantalla |
| 2 | Persistencia y autenticación | Sonnet | Registro y login reales contra Postgres, Argon2id, token de sesión |
| 3 | Personajes | Sonnet | Pantalla de 5 slots, crear/borrar/elegir, 3 clases, entrar al mundo |
| 4 | Mundo y movimiento autoritativo | **Opus** | Dos clientes se ven moverse fluido, con predicción, reconciliación, interpolación y AOI |
| 5 | Despliegue mínimo | Sonnet | Juego accesible en `wss://subdominio/ws` desde fuera de tu red |
| 6 | Inventario y equipamiento | Sonnet | Bolsas separadas de armas/armaduras, equipar, drag&drop, persistencia |
| 7 | Tiendas y armero | Sonnet | Comprar/vender con oro, stock, restock, log de economía |
| 8 | Granja y cultivos | Sonnet | Arar, plantar, regar, cosechar, crecimiento por tiempo, ciclo de día |
| 9 | Combate y monstruos | **Opus** | Monstruos con IA y aggro, daño, muerte, loot, respawn, PvE con varios jugadores |
| 10 | Progresión | Sonnet | XP, subida de nivel, stats por clase, primeras habilidades por clase |
| 11 | Chat y social | Sonnet | Canales global/zona/susurro, lista de jugadores, moderación básica |
| 12 | Pipeline de contenido y mapas | Sonnet | Mapas reales importados, atlas registry, validador de `content/`, assets CC0 integrados |
| 13 | Observabilidad y anticheat | **Opus** | Métricas, rate limiting completo, detección de anomalías, panel `/status` |
| 14 | Escalado multi-zona *(opcional)* | **Opus** | Zonas en hilos/procesos independientes, Redis, load test con 200 bots |
| 15 | Pulido y release | Sonnet | Audio, UX, opciones, launcher/parcheador, build de distribución |

---

## Detalle de entregables por fase

### Fase 1 — Esqueleto y primer handshake · **Opus**
Es corta pero define todos los cimientos: hazla con el modelo bueno.
- `Epimeteo.sln` con `Epimeteo.Shared`, `Epimeteo.Server` y el proyecto Godot en `client/`.
- `Opcode`, envelope binario, serialización MessagePack, `ITransport`.
- Servidor Kestrel: WebSocket en `:5100`, HTTP `/status` y `/version` en `:5101`.
- Bucle de tick a 20 Hz funcionando en vacío, con medición de tiempo de tick.
- Cliente Godot: escena de conexión, `WebSocketPeer`, `Hello`/`HelloAck`, `Ping` a 1 Hz, RTT en HUD.
- Máquina de estados de sesión con la tabla de opcodes legales.
- `.gitignore`, `.editorconfig`, `Directory.Build.props`.
- **Criterio de aceptación:** arrancas servidor, abres el cliente, ves "conectado, RTT 3 ms".

### Fase 2 — Persistencia y autenticación · Sonnet
- Docker Compose o instalación local de Postgres para desarrollo.
- `0001_init.sql` con accounts, account_sessions, login_attempts, characters, item_instances.
- DbUp ejecutando migraciones al arrancar.
- Argon2id, registro y login, tokens de sesión, rate limit por IP, log de intentos.
- Pantallas de login y registro en Godot con manejo de `ResultCode`.

### Fase 3 — Personajes · Sonnet
- `content/classes/*.json` con guerrero, mago e híbrido (stats iniciales pueden ser provisionales).
- Crear/borrar/listar/seleccionar con validación de nombre y de los 5 slots.
- Borrado lógico y transición a `Loading` → `InWorld` con `WorldEnter`.
- UI de selección de personaje con vista previa del sprite.

### Fase 4 — Mundo y movimiento autoritativo · **Opus**
La fase más importante del proyecto. Si sale bien, todo lo demás es sencillo.
- `Epimeteo.Shared/Simulation/MovementSystem.cs` usado literalmente por ambos lados.
- Mapa de colisión por tiles cargado desde `content/maps/`.
- Bucle: `InputState` → simulación → `Snapshot` con `lastAckedInputSeq`.
- Predicción, buffer de inputs y reconciliación con reejecución en cliente.
- Interpolación de entidades remotas con buffer de 100 ms.
- AOI por celdas de 16×16 con `EntitySpawn`/`EntityDespawn`.
- Guardado de posición cada 30 s y al desconectar.
- **Criterio de aceptación:** dos clientes en el mismo mapa, movimiento sin goma elástica,
  y con 150 ms de latencia simulada sigue siendo jugable.

### Fase 5 — Despliegue mínimo · Sonnet
Se hace pronto **a propósito**: descubrir problemas de red con 4 sistemas es fácil; con 12, no.
- **Primer paso obligatorio:** `sudo ss -lntp | grep -E ':(80|443|8080)\b'` en el servidor de
  producción para saber quién ocupa el 443. Ver la tabla de escenarios en `docs/00 §5`.
- `deploy/epimeteo.service` (systemd, usuario dedicado, `Restart=always`).
- `deploy/nginx-epimeteo.conf` con subdominio, TLS y `proxy_read_timeout` largo para el WS.
- Script de publicación (`dotnet publish` + rsync + restart) y de backup de Postgres.
- Configuración por entorno (`appsettings.Production.json`, secretos fuera de git).
- **Criterio de aceptación:** te conectas desde una red 4G externa y juegas.

### Fase 6 — Inventario y equipamiento · Sonnet
- `content/items/*.json` (armas, armaduras, consumibles, materiales, semillas).
- `InventorySystem` en servidor: mover, apilar, dividir, tirar, usar, equipar/desequipar.
- Contenedores 0/1/2/3 con sus reglas (una arma sólo entra en la bolsa de armas, etc.).
- Recálculo de stats derivados al equipar, `EquipmentUpdate`.
- UI: bolsas con pestañas, muñeco de equipo, drag & drop, tooltips.

### Fase 7 — Tiendas y armero · Sonnet
- `content/shops/*.json`, NPCs tenderos en el mapa, `ShopOpen` con validación de distancia.
- Compra/venta transaccional con `precioEsperado`, stock limitado y restock por tiempo.
- `economy_log` completo.
- UI de tienda y armero (el armero además repara: consume durabilidad y oro).

### Fase 8 — Granja y cultivos · Sonnet
Ciclo de **3 días reales**. Ver `docs/00-arquitectura.md §7` antes de empezar.
- `content/crops/*.json` con etapas visuales, `growthDays`, estación y rendimiento.
- Arar, plantar, regar, cosechar con validación de tile y de herramienta equipada.
- **Job diario a las 05:00 UTC**: dos `UPDATE` para toda la granja del servidor. Debe ser
  idempotente y recuperar días perdidos si el servidor estuvo caído (procesar N días de una vez).
- Regar acelera (+1,0 vs +0,5 de progreso), nunca mata el cultivo. `water_streak` → calidad.
- Reloj del mundo en tiempo real ligado a UTC; ciclo visual día/noche compartido por todos.
- `FarmTileUpdate` a los jugadores en AOI; render del tilemap de cultivos por etapa.
- **Criterio de aceptación:** plantas, adelantas el reloj del servidor 3 días, cosechas.

### Fase 9 — Combate, monstruos y PvP · **Opus**
- `content/monsters/*.json` + puntos de spawn en el mapa.
- Máquina de estados de IA: idle → patrulla → aggro → perseguir → atacar → volver.
- Fórmulas de daño en `Shared` (deterministas y testeadas), RNG con semilla de servidor.
- Cooldowns, alcance, líneas de visión, tabla de aggro multi-jugador.
- Muerte, saco de loot con derecho de saqueo, respawn temporizado.
- **PvP por región:** flags `safe`/`pvp` en `content/maps/`, validación de atacante **y** víctima,
  flag de combate de 10 s que bloquea logout y teleport, penalización de XP sin drop de inventario,
  `combat_log`. Lag compensation con historial de 500 ms y margen máximo de 200 ms.
- **Criterio de aceptación:** dos clientes se pegan en el bosque y no pueden hacerlo en la plaza,
  ni siquiera atacando desde el borde.

### Fase 10 — Progresión · Sonnet
- Curva de XP, subida de nivel, puntos de stat, stats por clase.
- 3–4 habilidades por clase con coste de maná y cooldown.
- Barra de habilidades y feedback visual.

### Fase 11 — Chat y social · Sonnet
- Canales global / zona / susurro / sistema, con rate limit y filtro básico.
- Lista de jugadores conectados, comandos `/w`, `/who`, `/help`.
- Comandos de admin: kick, ban, teleport, dar ítem (todos auditados).

### Fase 12 — Pipeline de contenido y mapas · Sonnet
- Integración real de los packs CC0, `ATTRIBUTIONS.md` cumplimentado.
- Atlas registry: `visualKey` del JSON → región de atlas, sin rutas en la lógica.
- Mapa del pueblo completo (Tiled o TileMapLayer de Godot) + 2 zonas exteriores.
- `tools/Epimeteo.ContentValidator` verificando que toda `def_key` referenciada existe.

### Fase 13 — Observabilidad y anticheat · **Opus**
- Serilog estructurado, métricas Prometheus (tick time, jugadores, mensajes/s, latencia de BD).
- Rate limiting completo por familia de opcode + strikes + desconexión.
- Detectores: velocidad, teletransporte, alcance imposible, cadencia de acciones, anomalías de oro.
- Endpoint `/status` autenticado y alertas.

### Fase 14 — Escalado multi-zona · **Opus** · *opcional*
**No la hagas por defecto.** Sin un objetivo de jugadores concreto, un solo proceso con las zonas
ya aisladas en hilos aguanta de sobra a un grupo de amigos y bastante más. Ábrela sólo si las
métricas de la Fase 13 muestran tick times que se disparan.
- Zonas en hilos independientes con paso de mensajes; traspaso de jugador entre zonas.
- Redis para presencia, chat global entre procesos y bloqueos de sesión.
- Preparación para partir en varios procesos con un gateway de sesión.
- `tools/Epimeteo.LoadTest`: 200 bots simulados; medir tick time y ancho de banda.

### Fase 15 — Pulido y release · Sonnet
- Audio, transiciones, menú de opciones, remapeo de teclas, pantalla completa.
- Launcher/parcheador contra `/files/` con manifiesto de hashes.
- Builds de distribución para Windows y Linux, primera versión etiquetada.

---

## Cómo abrir cada sesión

```
Lee CLAUDE.md y docs/STATUS.md. Vamos con la Fase N: <nombre>.
Escribe primero docs/fases/FASE-0N-<nombre>.md con el plan, lo revisamos, y luego implementas.
```

Y al cerrar:

```
Actualiza docs/STATUS.md con lo hecho, lo que falta y las decisiones tomadas. Sé breve.
```
