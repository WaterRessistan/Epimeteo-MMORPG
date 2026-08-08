# FASE 13 — Observabilidad y anticheat

> Modelo: **Opus** (CLAUDE.md §6): seguridad y anticheat son diseño, no implementación sobre algo
> ya cerrado.

## 1. Objetivo, y qué de esto ya está hecho

El roadmap (`docs/03`) pide cuatro cosas. Antes de planificar nada conviene ser honesto sobre
cuánto de eso **ya existe**, porque tres de las cuatro están parcialmente hechas desde fases
anteriores y planificarlas como si fueran nuevas sería trabajo duplicado:

| Lo que pide `docs/03` | Estado real al empezar esta fase |
|---|---|
| Serilog estructurado | **Hecho desde la Fase 1.** Consola + fichero rotativo, con propiedades estructuradas en todos los logs. |
| Rate limiting por familia + strikes + desconexión | **Hecho desde la Fase 1.** `SessionRateLimiter`: un cubo de fichas por `OpcodeFamily`, 3 strikes en 10 s → `Kick(RateLimited)`. |
| Detectores: velocidad, teletransporte, alcance imposible, cadencia | **Estructuralmente imposibles o ya validados**, ver §2 D3. |
| Métricas Prometheus, `/status` autenticado y alertas | **Nada.** Esto es el grueso real de la fase. |

Lo que esta fase añade de verdad:

1. **Métricas Prometheus** (`/metrics`): tick, jugadores, mensajes/s, latencia de BD.
2. **Cerrar `/status`**, que resultó estar **público en internet** — hallazgo real, §2 D2.
3. **Agregación de anomalías**: hoy cada rechazo se loguea y se olvida; nadie suma.
4. **Alertas**: umbrales que cambian el nivel de log, no una integración externa.

### Fuera de alcance, a propósito

- **Alertas a un canal externo** (email, Telegram, PagerDuty). Requiere credenciales y un servicio
  que decidir; el trabajo de esta fase es *producir* la señal (log estructurado de nivel `Error`
  con un evento propio, y métricas que Prometheus pueda alertar con sus propias reglas), no
  elegir por dónde sale.
- **Un Prometheus/Grafana desplegado.** Se expone `/metrics` en el formato estándar; instalar y
  configurar el scraper es trabajo de operación, no de esta fase. Se deja anotado en `STATUS.md`.
- **Baneo automático por anomalías.** La escalada llega hasta desconectar la sesión, no hasta
  banear la cuenta: un falso positivo que echa a alguien cuesta una reconexión, y uno que le
  banea la cuenta cuesta un jugador. Con los detectores recién estrenados y sin datos reales de
  cuántos falsos positivos producen, desconectar es el techo prudente. Banear sigue siendo manual
  (`/ban`, Fase 11).

## 2. Decisiones de diseño

### D1 — Métricas Prometheus a mano, sin dependencia nueva

`prometheus-net` es la opción obvia y está bien hecha. Aun así **no se añade**, por dos razones:
el formato de exposición de Prometheus es texto plano trivial (`# HELP`, `# TYPE`, `nombre{etiqueta="v"} valor`)
y las métricas que hace falta publicar son pocas y todas de tipos simples; y la tabla de stack de
CLAUDE.md §2 está cerrada y no incluye ninguna librería de métricas, así que añadir una es
reabrirla sin necesidad. El proyecto ya tiene precedente de infraestructura pequeña hecha a mano
donde la alternativa era una dependencia (`TokenBucket`, `TickMetrics`, `DeterministicRng`).

`MetricsRegistry` con `Counter` y `Gauge` (y un histograma mínimo para latencias, con *buckets*
fijos). Sin etiquetas dinámicas: cada métrica se declara una vez al arrancar. Si algún día hacen
falta etiquetas de cardinalidad alta, ahí sí compensa la librería — y se anota.

### D2 — `/status` está público: se cierra con token y se corta también en nginx

**Hallazgo real, comprobado antes de escribir este plan:**

```
$ curl https://epimeteo.waterressistan.duckdns.org/status
{"uptimeMs":4032121,"sessions":0,"world":{...},"tick":{...}}   → HTTP 200
```

`deploy/nginx-epimeteo.conf` proxifica `location /` entero al puerto 5101 para servir `/version`,
y `/status` viaja de rebote. Filtra jugadores conectados, profundidad de las colas de guardado y
tiempos de tick a cualquiera que pruebe la URL. No es catastrófico —no hay datos de jugador ni
forma de escribir— pero es reconocimiento gratis para quien quiera saber cuándo el servidor está
vacío o cargado, y es exactamente lo que el roadmap anticipaba con "endpoint `/status` autenticado".

Se arregla en **dos capas independientes**, porque cada una sola es frágil:

1. **Token en el servidor**: `/status` y `/metrics` exigen `Authorization: Bearer <token>`
   (`Epimeteo:MetricsToken`, fuera de git como la cadena de conexión). Sin token configurado, los
   endpoints responden 404 en vez de abrirse: *fallar cerrado*. `/version` sigue abierto — es
   justo lo que un cliente necesita para saber si tiene que actualizarse antes de conectar.
2. **nginx deja de proxificar rutas internas**: `location = /version` explícito en vez de
   `location /`. Aunque el token se filtrara, `/status` deja de ser alcanzable desde fuera.

La comparación del token se hace en tiempo constante (`CryptographicOperations.FixedTimeEquals`):
comparar con `==` filtra el token por tiempo de respuesta, y aquí no cuesta nada evitarlo.

### D3 — Los detectores que pide el roadmap: dos son imposibles por diseño, dos ya existen

Merece la pena escribirlo porque "implementar un detector de teletransporte" sonaría razonable y
sería **trabajo inútil, o peor: un detector que no puede dispararse dando falsa sensación de
cobertura**.

- **Velocidad**: imposible. Desde FASE-04 §2 D1 el input es de paso fijo — un input = un tick =
  50 ms exactos, y el `dtMs` del cliente **no se integra**. La velocidad la fija el servidor. El
  presupuesto de `InputQueue` (20/s, ráfaga 6) limita cuántos inputs se aceptan, que con paso fijo
  *es* la velocidad. Ya suma strikes y ya desconecta.
- **Teletransporte**: imposible. El cliente nunca manda posiciones (CLAUDE.md §4); el servidor
  integra el movimiento él mismo y el único salto de posición que existe lo produce el propio
  servidor (`Zone.Teleport`, respawn y `/teleport` de admin). No hay superficie que vigilar.
- **Alcance imposible**: ya validado en todas las acciones con objetivo — combate
  (`CombatSystem.ValidateAttack`), tiendas (3 tiles), granja (2), loot (2). Lo que **falta** no es
  la validación, es que nadie *cuenta* cuántas veces la falla la misma sesión.
- **Cadencia de acciones**: ya la cubre `SessionRateLimiter` por familia.

Conclusión: el hueco real no está en detectar, está en **agregar**. Y eso es D4.

### D4 — `AnomalyRecorder`: lo que falta no es detectar, es sumar

Hoy hay ~29 puntos en `GameWorld` que rechazan una acción y mandan un `SystemMessage`, más los
rechazos de `InputQueue` y `SessionRateLimiter`. Cada uno se resuelve solo y se olvida. Un cliente
honesto falla alguno de vez en cuando (latencia: pides atacar a algo que acaba de morir); un
cliente parcheado falla **el mismo** cientos de veces por minuto. Nadie mira esa diferencia.

`AnomalyRecorder`: por sesión, un contador por `AnomalyKind` en una ventana deslizante de 60 s.

- `AnomalyKind`: `OutOfRange`, `RateLimited`, `InputBudget`, `InvalidState`, `ProtocolError`,
  `EconomyRejected`.
- Umbrales por tipo (no uno global: 30 `OutOfRange` en un minuto es sospechoso, 30 `ProtocolError`
  es un cliente roto o malicioso y debería costar más caro).
- Escalada en tres pasos: **contar** en silencio → al cruzar el umbral de aviso, un log de nivel
  `Warning` con evento propio (`Anomalía {Kind} …`) **y** una fila en `anomaly_log` → al cruzar el
  umbral duro, `Kick` y la fila queda marcada.
- Es **puro y determinista**: recibe `nowMs`, no consulta reloj. Se puede testear el escalado
  exacto sin esperar un minuto real, mismo criterio que `TokenBucket`/`DeterministicRng`.

Los umbrales son **provisionales y a propósito generosos**: sin datos reales de cuántas anomalías
produce un jugador honesto con mala conexión, apretarlos es inventar. Empiezan altos, y `anomaly_log`
existe justamente para poder ajustarlos con datos en vez de con intuición.

### D5 — La latencia de BD se mide donde se abre la conexión, no en cada repositorio

`NpgsqlConnectionFactory.OpenAsync` es el único punto por el que pasan las ~10 clases de
repositorio. Instrumentarlo ahí mide *abrir conexión* (que es lo que se degrada primero cuando
Postgres sufre) sin tocar diez ficheros ni obligar a cada repositorio futuro a acordarse. La
latencia de las consultas en sí queda para cuando haya una consulta lenta que investigar — con
`pg_stat_statements`, que ya lo hace mejor que instrumentar a mano.

### D6 — Las alertas son log estructurado, no una integración

Un umbral cruzado escribe un log de nivel `Warning`/`Error` con un evento identificable y campos
estructurados. Eso es *la alerta*: quien opere el servidor la engancha a lo que use (journald →
`systemd`, o Prometheus con sus propias reglas sobre `/metrics`). Elegir aquí un canal concreto
sería una decisión de operación disfrazada de código, y ninguna de las opciones es reversible sin
tocar el servidor.

### D7 — `anomaly_log`: tabla nueva, mismo patrón de sink que todo lo demás

Hueco real de esquema: `docs/02` no previó nada para esto (`combat_log` es PvP, `admin_action_log`
es de la Fase 11). Cola acotada + `IHostedService`, igual que los otros cinco sinks. `DropOldest`:
bajo una inundación de anomalías, perder algunas filas es preferible a que el tick espere — y si
hay inundación, el patrón ya quedó registrado en las primeras.

## 3. Migración de BD — `0007_anomaly_log.sql`

`anomaly_log(id, at, session_id, character_id, account_id, kind, count_in_window, action_taken, remote_address, details jsonb)`.

## 4. Servidor

| Fichero | Qué es |
|---|---|
| `Observability/MetricsRegistry.cs`, `Counter.cs`, `Gauge.cs`, `Histogram.cs` | El registro y sus tres tipos (D1). |
| `Observability/ServerMetrics.cs` | Las métricas concretas del juego, declaradas una vez. |
| `Security/AnomalyKind.cs`, `AnomalyRecorder.cs` | La agregación y su escalada, puras (D4). |
| `Persistence/Anomalies/` | `AnomalySave`/`IAnomalySink`/`AnomalyRepository`/`AnomalySaver` (D7). |
| `Net/Session.cs`, `SessionManager.cs` | Alimentan métricas y anomalías. |
| `Persistence/NpgsqlConnectionFactory.cs` | Histograma de latencia de apertura (D5). |
| `Program.cs` | `/metrics` nuevo, `/status` y `/metrics` autenticados (D2). |
| `ServerOptions.cs` | `MetricsToken`. |

## 5. Despliegue

`deploy/nginx-epimeteo.conf`: `location = /version` en vez de `location /` (D2).

## 6. Cliente Godot

Ninguno. Esta fase no toca nada que el cliente vea.

## 7. Tests

`Server.Tests`: `MetricsRegistryTests` (formato de exposición exacto, incluidos histogramas y
`_bucket`/`_sum`/`_count`), `AnomalyRecorderTests` (escalada exacta por tipo, ventana deslizante,
que un tipo no contamina a otro, que por debajo del umbral no pasa nada).

## 8. Verificación sin Godot

`tools/Epimeteo.WorldBot --anticheat`: un bot que fuerza rechazos **reales** contra el servidor de
producción (atacar fuera de alcance repetidamente) y comprueba que la escalada ocurre de verdad:
avisos primero, desconexión después, y fila en `anomaly_log`. Más `curl` a `/status` y `/metrics`
sin token (404), con token (200), y desde internet (inalcanzable tras el cambio de nginx).

## 9. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde.
2. `/metrics` responde en formato Prometheus válido, con tick, jugadores, mensajes y latencia de BD.
3. `/status` y `/metrics` **sin** token → 404; con token → 200. Sin token configurado → 404 (falla cerrado).
4. `https://<dominio>/status` deja de ser alcanzable desde internet; `/version` sigue abierto.
5. Un bot que insiste en una acción inválida acaba desconectado por la escalada, con fila en
   `anomaly_log`; un bot honesto en la misma corrida no.
6. El tick medio no se degrada de forma apreciable por la instrumentación.

---

## 11. Resultado y hallazgos reales

### El hallazgo de seguridad, confirmado y cerrado

`/status` respondía **200 en internet**, sin autenticar, filtrando jugadores conectados,
profundidad de las colas de guardado y tiempos de tick. Comprobado con `curl` antes de escribir el
plan (§2 D2) y cerrado en las dos capas previstas:

```
Antes:   curl https://<dominio>/status   → 200 + JSON completo
Después: curl https://<dominio>/status   → 404
         curl https://<dominio>/metrics  → 404
         curl https://<dominio>/version  → 200   (sigue abierto, es lo que el cliente necesita)
         curl https://<dominio>/ws       → 400   (el juego sigue vivo; falta el upgrade)
```

En loopback, las seis combinaciones se comprobaron una a una: `/version` sin token → 200;
`/status` y `/metrics` sin token → 404; `/status` con token equivocado → 404; `/status` y
`/metrics` con el token bueno → 200.

El parche de nginx se aplicó **quirúrgicamente sobre el fichero instalado**, no copiando el del
repositorio encima: Certbot le añade sus propias líneas de `ssl_certificate` al pedir el
certificado (lo dice la cabecera del propio fichero) y sobrescribirlo habría dejado el sitio sin
TLS. Con copia de seguridad previa (`epimeteo.bak-fase13`) y `nginx -t` antes de recargar.

### Lo que el roadmap pedía y ya estaba hecho

Tres de las cuatro líneas de `docs/03` para esta fase estaban parcial o totalmente cubiertas desde
fases anteriores (§1). Merece la pena repetir la conclusión de §2 D3 porque es lo que evitó el
trabajo inútil: **de los cuatro detectores que pedía el roadmap, dos son imposibles por diseño**
—velocidad y teletransporte, porque el cliente nunca manda posiciones y el input es de paso fijo
desde la Fase 4— **y dos ya estaban validados** en cada acción. Implementar "un detector de
teletransporte" habría sido escribir código que no puede dispararse nunca, dando falsa sensación
de cobertura. El hueco real no estaba en detectar: estaba en **agregar**, y eso es lo que añade
`AnomalyRecorder`.

### Un hallazgo de la herramienta, preexistente y no arreglado aquí

Durante la primera corrida de `--anticheat`, el bot honesto también acabó desconectado — pero con
`KickReason.InternalError`, no por el anticheat: se le llenó la cola de salida de la sesión
(`OutboundQueueCapacity`, 256 frames) y el servidor la cerró, que es exactamente lo que la Fase 1
diseñó que hiciera con una sesión atascada.

**No lo causa esta fase.** El log de producción lo registra 16 veces en dos días, la primera el
7 de agosto durante la verificación PvP de la Fase 9, mucho antes de tocar nada de esto. Es el
arnés de bots, que ejecuta varios clientes en un proceso y a veces no drena a tiempo; el servidor
se comporta como debe. Se deja anotado sin arreglar: arreglar el arnés no es trabajo de una fase
de observabilidad, y disimularlo hubiera sido peor.

**Lo que sí se arregló es la comprobación.** El primer `Check("el bot honesto no se ve afectado",
honesto.Kicked is null)` pasó **por suerte de temporización**: el `S2CKick` todavía estaba en
vuelo cuando se comprobó. Un verde que depende de que un paquete no haya llegado todavía no
prueba nada. Se cambió a drenar primero y luego afirmar lo que de verdad importa —que la escalada
*de anomalías* no lo alcanzó (`Kicked != RateLimited`)— en vez de un `is null` frágil que fallaría
por un motivo ajeno. En la corrida final el honesto sobrevivió de verdad (`sigue dentro`).

### Verificación real ejecutada

1. ✅ `dotnet build` sin warnings (solución y cliente Godot); `dotnet test`: **163/163 compartidos
   + 305/305 servidor**.
2. ✅ `/metrics` en formato Prometheus válido: contadores, gauges e histogramas con sus
   `_bucket{le=…}` acumulativos, `_sum` y `_count`.
3. ✅ Los seis casos de autenticación de §11, y `/status` inalcanzable desde internet.
4. ✅ `tools/Epimeteo.WorldBot --anticheat`: **4/4 en verde**. Con 25 rechazos (por debajo del
   umbral de aviso) la sesión sigue viva; insistiendo hasta 120 se desconecta; el bot honesto de
   la misma corrida no lo paga.
5. ✅ En `psql`, `anomaly_log` con **exactamente dos filas** por corrida y ninguna de más: una con
   `count_in_window = 30, action_taken = 1` (aviso) y otra con `120, 2` (desconexión) — la
   escalada de dos pasos funcionando tal cual se diseñó. En el log, `WRN` en el aviso y `ERR` en
   la desconexión, que es *la alerta* de D6.
6. ✅ Coste de la instrumentación, comparando `/status` antes y después: tick medio **10 → 11 µs**,
   p99 **41 → 33 µs**, 1 overrun en 6984 ticks. Dentro del ruido; el presupuesto de un tick a
   20 Hz son 50 000 µs.

**Límite honesto:** los umbrales (`AnomalyThresholds`) son provisionales y generosos a propósito,
y **no se han validado contra tráfico real de jugadores**, porque no hay ninguno todavía. Están
puestos para no producir falsos positivos, no para atrapar a nadie con eficacia — y `anomaly_log`
existe justamente para poder apretarlos con datos cuando los haya. Un bot que se quede
deliberadamente por debajo de 30 rechazos por minuto no dispara nada, y eso es una decisión
consciente, no un descuido.
