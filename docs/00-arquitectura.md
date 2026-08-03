# 00 — Arquitectura

## 1. Elección de stack y justificación

### Cliente: Godot 4.5 con .NET (C#)

- Tu experiencia en C# se transfiere íntegra. La curva es de API, no de lenguaje.
- El renderizador 2D de Godot es 2D **de verdad** (no un 3D con la Z aplastada como Unity):
  `TileMapLayer`, Y-sort, iluminación 2D, `snap_2d_transforms_to_pixel` y viewport de baja
  resolución escalado a entero. Es exactamente el pipeline que pide el pixel art nítido.
- Exportación a Linux/Windows/Mac sin licencias ni servicios en la nube de terceros. Encaja con
  autoalojamiento: tú controlas el binario y el CDN de parches.
- **La razón decisiva:** siendo el cliente C# y el servidor C#, ambos pueden referenciar la
  **misma librería compartida** con el código de movimiento, colisión y fórmulas de combate.
  Eso es lo que hace viable la predicción del lado cliente sin duplicar (y desincronizar) lógica.
  Con un cliente en GDScript o un servidor en Node/Go pierdes esto.

Descartado Unity: el licenciamiento y el peso del editor no aportan nada aquí, y su 2D sigue
siendo una capa sobre 3D. Descartado un cliente web (Phaser/PixiJS): perderías el compartir
código con el servidor y complica el anticheat, aunque WebSocket deja la puerta abierta a un
export web futuro.

### Servidor: .NET 8 headless, **sin Godot**

- El servidor no necesita renderizar nada. Correr Godot en modo headless te obligaría a arrastrar
  el runtime del motor, su bucle de frames y su modelo de escenas en un proceso que sólo debe
  simular. Un proceso .NET puro arranca en ms, consume ~50 MB y tiene un bucle de tick que tú
  controlas al milisegundo.
- El mundo es **tile-based**. La colisión es un grid + AABB, ~200 líneas. No necesitas un motor
  de físicas, y por tanto no necesitas el motor.
- .NET 8 te da `System.Threading.Channels`, `Span<T>`, GC de servidor y `System.Net.WebSockets`
  nativo. Rendimiento de sobra para miles de conexiones en un solo VPS.
- Se despliega como un binario + unit de systemd. Nada de contenedores obligatorios.

### Transporte: WebSocket binario sobre TLS

- Ya tienes 443 ocupado por nginx (o similar). WebSocket entra por el mismo 443 vía subdominio
  y `proxy_pass`, sin abrir puertos nuevos en el firewall ni pelearte con NAT o UDP.
- TLS gratis con el certbot que ya usas.
- Es TCP, luego ordenado y fiable: **para un RPG top-down a 20 Hz con predicción e interpolación
  es suficiente**. UDP importa en shooters competitivos, no aquí.
- Si algún día la latencia lo exige, el diseño está preparado: la capa de transporte está detrás
  de una interfaz `ITransport` y se puede añadir ENet/KCP por UDP en un puerto propio sin tocar
  el resto. **No lo hagas antes de tener un problema medido.**

### PostgreSQL + Dapper (no EF Core)

- Postgres por `jsonb`, índices parciales, `citext`, transacciones serias y porque ya corre bien
  en tu Ubuntu.
- Dapper y no EF Core porque en un servidor de juego quieres SQL explícito, sin change-tracking
  ni contextos compartidos entre hilos, y con control total sobre cuándo y qué se escribe. Las
  escrituras van por una **cola de persistencia** fuera del hilo de simulación.

---

## 2. Topología

```
        Internet
           │  443/tcp (ya en uso por nginx)
    ┌──────▼─────────────────────────────┐
    │ nginx  juego.tudominio.com         │
    │  /        → :5101  (API HTTP)      │
    │  /ws      → :5100  (WebSocket)     │
    │  /files/  → estáticos (parches)    │
    └──────┬───────────────┬─────────────┘
           │ loopback      │ loopback
    ┌──────▼───────┐  ┌────▼──────────┐
    │ Epimeteo.Server (un proceso)     │
    │  ┌────────────┐ ┌──────────────┐ │
    │  │ Net layer  │ │ HTTP API     │ │   Kestrel + WebSockets
    │  │ sesiones   │ │ auth/version │ │
    │  └─────┬──────┘ └──────┬───────┘ │
    │   colas│               │         │
    │  ┌─────▼───────────────▼───────┐ │
    │  │ WorldService: N zonas       │ │   1 hilo de tick por zona
    │  │  tick 20 Hz · AOI · sistemas│ │
    │  └─────┬───────────────────────┘ │
    │  ┌─────▼───────────┐             │
    │  │ Persistence Q   │  async      │
    │  └─────┬───────────┘             │
    └────────┼─────────────────────────┘
             │ 5432
        ┌────▼──────┐      ┌──────────┐
        │ PostgreSQL│      │ Redis    │  (fase 14)
        └───────────┘      └──────────┘
```

Un solo proceso hasta la Fase 14. Las zonas ya están separadas como objetos con su propio hilo
desde la Fase 4, de modo que partirlas en procesos distintos más adelante no es un rediseño.

---

## 3. Reparto de responsabilidades

### Sólo servidor (autoritativo)
Posición y colisión reales · velocidad y cooldowns · HP/MP/estados · daño, aggro e IA de monstruos
· loot y drop rates · contenido del inventario y equipo · oro y todas las transacciones · stock de
tiendas · estado y crecimiento de cultivos · XP y nivel · quién ve a quién (AOI) · RNG (semilla
en servidor; el cliente nunca tira dados) · tiempo del mundo y ciclo día/noche.

### Sólo cliente
Render, cámara, animación, partículas, audio · UI y su estado local · lectura de input ·
**predicción** del propio movimiento · **interpolación** de las demás entidades · efectos
cosméticos inmediatos (feedback de swing, sonido de paso) · caché de definiciones de contenido.

### Compartido (`Epimeteo.Shared`)
Enums y opcodes · DTOs de mensajes · `MovementSystem` (misma función aplicada al mismo input
produce el mismo resultado en ambos lados) · geometría de colisión contra tiles · fórmulas
puras de combate y economía · constantes de tick y velocidades · POCOs de definiciones de contenido.

> Regla: si el cliente necesita *predecir* algo, ese algo va en `Shared`. Si el cliente sólo
> necesita *mostrarlo*, va en `Server` y viaja por la red.

---

## 4. El bucle de red

### Servidor — tick a 20 Hz (50 ms)

```
por cada zona, cada 50 ms:
  1. drenar la cola de mensajes entrantes de la zona
  2. aplicar inputs de jugadores (con validación de dt y anti-speedhack)
  3. simular: movimiento, IA de monstruos, combate, timers, respawns
  4. resolver cultivos vencidos (lazy: sólo los tocados)
  5. recalcular AOI de quien se ha movido de celda
  6. construir y encolar snapshots por jugador (delta contra lo último confirmado)
  7. encolar escrituras sucias hacia la cola de persistencia
```

Los snapshots salen a **10 Hz** (uno de cada dos ticks) para reducir ancho de banda; los eventos
discretos (daño, chat, loot, resultado de compra) se envían **inmediatamente**, sin esperar al tick
de snapshot.

### Cliente — cada frame

```
1. leer input → construir InputState { seq, dirX, dirY, botones, dtMs }
2. aplicar Shared.MovementSystem localmente al jugador (PREDICCIÓN)
3. guardar (seq, input, posResultante) en un buffer circular
4. enviar InputState al servidor (a 20 Hz, no cada frame)
5. al recibir snapshot con lastAckedSeq y posAutoritativa:
     - descartar del buffer todo lo <= lastAckedSeq
     - si |posAutoritativa - posPredichaEnEseSeq| > 0.05 tiles:
         reposicionar a posAutoritativa y REEJECUTAR los inputs pendientes (RECONCILIACIÓN)
6. otras entidades: renderizar en (ahora - 100 ms) interpolando entre los dos snapshots
   que rodean ese instante (INTERPOLACIÓN)
```

100 ms de buffer de interpolación = tolera perder un snapshot sin que las entidades se congelen.

### Área de interés (AOI)

El mundo se divide en celdas de **16×16 tiles**. Cada jugador está suscrito a su celda y a las 8
adyacentes. Sólo recibe entidades y eventos de esas 9 celdas. Al cruzar de celda se envían
`EntitySpawn` de lo que entra y `EntityDespawn` de lo que sale. Esto es lo que separa un juego que
aguanta 20 jugadores de uno que aguanta 500.

### Handshake y máquina de estados de la sesión

```
Connecting → Authenticated → CharacterSelect → Loading → InWorld → Disconnected
```

Cada opcode declara en qué estados es legal. Un mensaje recibido en un estado ilegal cierra la
conexión y se registra. Esto elimina de golpe una familia entera de exploits.

### Persistencia

Nada se escribe en BD dentro del tick. Las entidades sucias se marcan y una tarea async vuelca:
- posición y stats del personaje: cada **30 s** y al desconectar/cambiar de zona,
- inventario, equipo y oro: **inmediatamente** tras cada transacción (transacción SQL atómica),
- cultivos: al plantar/regar/cosechar,
- log de economía: append-only, siempre.

---

## 5. Manejo de puertos y despliegue

Nada del juego se expone directamente. `Epimeteo.Server` bindea `127.0.0.1:5100` y `127.0.0.1:5101`.

> **Pendiente de confirmar (Fase 5):** no está claro qué proceso ocupa el 443 en el servidor de
> producción — puede ser nginx o una app Python sirviendo directamente. Diagnóstico:
> `sudo ss -lntp | grep -E ':(80|443|8080)\b'`. Tres escenarios:
>
> | Quién tiene el 443 | Qué hacemos |
> |---|---|
> | nginx / Caddy / Apache | Añadir un `server` block para el subdominio. **Preferido.** |
> | Python (gunicorn/uvicorn) directo | Poner nginx delante de ambos, o mover el juego a un puerto propio con TLS de Kestrel |
> | Nada, sólo 80 ocupado | Exponer `7777/tcp` con TLS gestionado por Kestrel |

Con proxy inverso, publicando `juego.tudominio.com` en el 443 existente:

- `location /ws` con `proxy_http_version 1.1`, cabeceras `Upgrade`/`Connection`,
  `proxy_read_timeout 3600s` (si no, nginx corta la partida al minuto).
- `location /` para la API HTTP (login, `/version`, `/status`).

Alternativa si no quieres tocar nginx: exponer `7777/tcp` directamente con TLS gestionado por
Kestrel. Es más simple pero pierdes el certificado compartido y el rate limiting de nginx.
**Recomendación: nginx + subdominio.**

Servicio systemd `epimeteo.service` con `Restart=always`, usuario dedicado sin shell,
`ProtectSystem=strict` y `WorkingDirectory` en `/opt/epimeteo`.

---

## 6. Zonas y PvP

El pueblo es zona segura; las zonas de farmeo tienen PvP activo. Esto se modela como **flags de
región**, no como una propiedad global del mapa: un mismo mapa puede tener un claro seguro dentro
de un bosque hostil.

En `content/maps/<mapa>.json`:

```jsonc
"regions": [
  { "name": "plaza",        "rect": [0,0,64,48],   "flags": ["safe", "no_monsters"] },
  { "name": "bosque_norte", "rect": [0,48,64,120], "flags": ["pvp", "outdoor"] }
]
```

Reglas de resolución, todas en servidor:

1. `CanAttack(atacante, víctima)` exige que **ambos** estén en región con flag `pvp`, según las
   posiciones **autoritativas**. Si uno de los dos pisa zona segura, el ataque se rechaza con
   `SafeZone`. Esto elimina el clásico exploit de disparar desde dentro de la zona segura.
2. Al entrar en combate PvP se aplica un **flag de combate de 10 s** que impide desconectar
   limpiamente (el personaje se queda en el mundo 10 s tras el logout) y bloquea el teletransporte.
3. La víctima no dropea inventario al morir (decisión de diseño por defecto; el full-loot ahuyenta
   a los jugadores nuevos). Penalización: pérdida de un % de XP del nivel actual y reaparición en el
   pueblo. Ajustable en `content/` sin tocar código.
4. Todo kill PvP se registra en `combat_log` para investigar griefing.
5. El cliente recibe los flags de región en `WorldEnter` y un `ZoneFlagsUpdate` al cruzar de región,
   sólo para pintar el aviso de "zona hostil". **Nunca decide nada.**

Consecuencia para el netcode (Fase 9): con PvP, la latencia deja de ser cosmética. Se usa
**lag compensation ligera**: el servidor guarda 500 ms de historial de posiciones y valida el
alcance de un ataque contra la posición que la víctima ocupaba en `now - RTT/2`, con un margen
máximo de 200 ms. Más allá de eso, se valida contra la posición actual.

---

## 7. Reloj del mundo y granja en tiempo real

Los cultivos tardan **~3 días reales**. Esto tiene tres consecuencias de arquitectura:

1. **El crecimiento no se simula.** Cada tile guarda su progreso y el instante del último cambio.
   Nada se ejecuta mientras nadie mira.
2. **Existe un tick diario, no un tick por tile.** A las **05:00 UTC** (frontera del "día de
   granja", elegida para caer en la madrugada española) un job hace *una* sentencia `UPDATE` sobre
   toda la tabla `farm_tiles`: suma el progreso del día y limpia el flag de regado. Miles de
   parcelas cuestan una consulta.
3. **Regar acelera, no salva.** Un día regado suma 1 día de progreso; sin regar suma 0,5. El
   cultivo **nunca muere** por descuido. Con ciclos de 3 días reales, matar cosechas por no
   conectarse un día castiga precisamente al jugador casual que este diseño quiere retener.
   Un cultivo de 3 días regado a diario tarda 3; abandonado, 6. La constancia se premia, no se exige.
   *(Parametrizable en `content/crops/`; si quieres marchitado real, se activa ahí.)*

El **ciclo visual día/noche va en tiempo real**, atado al reloj UTC del servidor: todos los
jugadores comparten el mismo cielo. Es coherente con la granja en tiempo real y da sensación de
mundo persistente compartido. El servidor difunde la hora en `WorldEnter` y el cliente la
extrapola sola, sin tráfico adicional.

---

## 8. Decisiones que se dejan explícitamente para después

| Tema | Fase | Nota |
|---|---|---|
| UDP/ENet | — | sólo si se mide un problema real de latencia |
| Redis / multi-proceso | 14 | las zonas ya están aisladas desde la fase 4 |
| Sharding por mapa en varias máquinas | 14+ | requiere un gateway de sesión |
| Launcher/parcheador | 15 | `/files/` en nginx + manifiesto de hashes |
| Guilds, mail, trade entre jugadores | post-15 | el esquema de BD ya deja hueco |
