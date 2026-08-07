# FASE 11 — Chat y social

> Modelo: **Sonnet** (CLAUDE.md §6): implementación sobre diseño ya cerrado. `ChatSend`/`ChatMessage`
> (`0x0070`/`0x8070`) están reservados desde la Fase 1, sin tipar hasta ahora — mismo caso que
> `SkillCast` en las Fases 9-10. `chat_log` está diseñada en `docs/02-esquema-bd.md` desde la Fase 0,
> sin migrar todavía.

## 1. Objetivo

Canales de chat de verdad (global, zona, susurro), lista de jugadores conectados y comandos de
barra: `/w`, `/who`, `/help`, y los de administración (`/kick`, `/ban`, `/teleport`, `/give`),
todos auditados.

### Fuera de alcance, a propósito

- **Canal `system` sin uso todavía.** Reservado en el enum para avisos del servidor (mantenimiento,
  eventos); nadie lo emite esta fase.
- **`/ban` y `/kick` sólo alcanzan a quien está conectado ahora mismo.** Banear a un jugador
  desconectado exigiría resolver nombre → cuenta por SQL desde el hilo de red antes de encolar la
  acción; el resto de los cuatro comandos de admin (kick, teleport, give) ya necesitan que el
  objetivo esté online por su propia naturaleza (no se puede teletransportar ni dar un ítem a
  quien no tiene entidad ni inventario cargado), así que exigirlo también para ban mantiene los
  cuatro con la misma regla en vez de que uno se comporte distinto. Un panel de admin de verdad
  (mencionado en `docs/02` para revocar `account_sessions`) es fase futura.
- **Sin salas de guild ni listas de amigos.** `docs/02` los deja anotados como "espacio reservado,
  no implementar todavía"; esta fase no los toca.
- **Filtro de palabras deliberadamente básico.** Una lista fija en el servidor, no un servicio de
  moderación. Lo que fija esta fase es que el filtro existe y censura antes de retransmitir, no
  qué tan bueno es.

## 2. Decisiones de diseño

### D1 — Sin opcodes nuevos

`ChatSend`/`ChatMessage` ya estaban reservados; se tipan por primera vez (igual que `SkillCast` en
la Fase 9). Los comandos de barra (`/w`, `/who`, `/help`, admin) **no** son opcodes aparte: viajan
como texto normal dentro de `ChatSend.Text` y el servidor los reconoce por el prefijo `/`. Evita
inflar el catálogo con un opcode por comando y es el patrón habitual de chat de MMO. El protocolo
no sube de versión: nada existente cambia de forma.

### D2 — `C2SChatSend { Channel, Text }`, sin campo de destinatario

`Channel` es `Global` o `Zone` para un mensaje normal — el cliente lo elige (tecla que alterna).
Para susurrar no hace falta un campo `TargetName` aparte: `/w Nombre mensaje` ya lleva el
destinatario dentro del texto, y el servidor ignora `Channel` en cuanto `Text` empieza por `/`. Un
campo de destinatario que sólo se usara a veces habría sido peor diseño que reutilizar el propio
texto.

### D3 — Los comandos se parsean en el servidor, no en el cliente

`Server/Chat/ChatCommandParser.cs` (puro, testeado): recibe el texto y devuelve un
`ChatCommand` — `Say`, `Whisper`, `Who`, `Help`, `Kick`, `Ban`, `Teleport`, `Give` o `Invalid`. El
cliente no necesita saber qué comandos existen; si escribe uno que no reconoce, el servidor
contesta con `SystemMessage`. Mismo reparto Shared/Server-puro-vs-orquestación que
`CombatSystem`/`SkillSystem`.

### D4 — Susurro: se busca por nombre en todas las zonas, no en la actual

`GameWorld` sólo tiene una zona hoy (`map.village`), pero está escrito para varias desde la
Fase 4. Buscar el nombre en todas, no sólo en la del que susurra, es lo correcto aunque hoy dé
igual — y es exactamente el mismo criterio que ya usa `KickSession`/`FindPlayer`.

### D5 — Los cuatro comandos de administrador exigen objetivo conectado y se resuelven en el tick

`Kick`/`Teleport`/`Give` sólo tienen sentido con el objetivo cargado en memoria (entidad, posición,
inventario). `Ban` se alinea con ellos por consistencia (D del "fuera de alcance" de arriba) en vez
de ser el único que acepta un nombre sin verificar que existe de verdad. Los cuatro se resuelven
igual: `FindPlayerByName` en el tick, sin tocar Postgres desde ahí (CLAUDE.md §4).

### D6 — Cuenta de administrador: columna nueva, cargada una vez en el login

`accounts` no tenía ninguna columna de rol. Se añade `is_admin boolean not null default false`
(hueco real de esquema, mismo criterio que los de protocolo). Se lee una sola vez en
`AuthService.LoginAsync` (ya carga la fila de la cuenta) y viaja en `AuthOutcome` → `Session.IsAdmin`
→ `WorldJoinRequest.IsAdmin` → `PlayerEntity.IsAdmin`: el mismo camino que ya usan
`StatPoints`/`Gold`/etc. desde `CharSelect`. Ninguna cuenta puede autopromocionarse: se concede a
mano por SQL (`UPDATE accounts SET is_admin = true WHERE username = '...'`), a propósito.

### D7 — Auditoría de admin: un sink nuevo, mismo patrón que `combat_log`/`economy_log`

`AdminActionSave`/`IAdminActionSink`/`AdminActionSaver`/`AdminActionRepository`: cola acotada +
`IHostedService` que vacía a Postgres, igual que el resto de sinks desde la Fase 6. `Ban` hace además
el `UPDATE accounts` real dentro del mismo `AdminActionSaver.ProcessAsync` — no hace falta un sink
aparte sólo para esa mutación, ya está en el sitio correcto (fuera del tick, async).
`Kick`/`Teleport`/`Give` sólo dejan constancia; su efecto de verdad ya ocurrió en memoria (kick
cierra la sesión al instante, teleport mueve al admin, give ya pasó por `InventorySystem` normal —
el ítem se persiste solo, por el `InventorySaver` que ya existe).

### D8 — `/teleport <nombre>` mueve al admin, no al objetivo

Convención habitual de herramientas de GM: "llévame a quien tengo que mirar", no "tráelo a él".
Reutiliza `Zone.Teleport`, el mismo método que usa `Respawn` desde la Fase 9.

### D9 — `chat_log` guarda el texto sin censurar; lo que se retransmite sí va censurado

El registro es para moderación (`docs/02`): un moderador necesita ver lo que se escribió de
verdad, no la versión con asteriscos. El filtro (D del "fuera de alcance") sólo censura lo que
llega a los demás jugadores.

### D10 — Límite de longitud, en `Shared` para que el cliente también lo aplique

`ChatConstants.MaxMessageLength`. Un mensaje más largo o vacío se trata como payload inválido —
mismo criterio que el resto del protocolo (`Kick(ProtocolError)`), no un rechazo silencioso.

## 3. Migración de BD — `0006_chat_social.sql`

- `accounts.is_admin boolean not null default false` (D6).
- `chat_log` tal como la diseña `docs/02` (D9).
- `admin_action_log(id, at, admin_character_id, admin_name, target_character_id, target_name, action, reason, details jsonb)` (D7) — no estaba en `docs/02`, hueco real de esta fase.

## 4. `Shared`

| Fichero | Qué es |
|---|---|
| `Net/ChatChannel.cs` | `Global`/`Zone`/`Whisper`/`System` (D1). |
| `Net/Messages/C2SChatSend.cs`, `S2CChatMessage.cs` | Tipados por primera vez (D2). |
| `Data/ChatConstants.cs` | `MaxMessageLength` (D10). |
| `Net/ResultCode.cs` | `NotAuthorized = 612`, `InvalidCommand = 613` — huecos reales, siguientes valores libres del bloque ya reservado (mismo criterio que `NoStatPointsAvailable`/`SkillNotUnlocked` en la Fase 10). `TargetNotFound` se reutiliza para "no hay nadie con ese nombre conectado". |

## 5. Servidor

- `Chat/ChatFilter.cs` (puro): censura una lista fija de palabras.
- `Chat/ChatCommand.cs`, `ChatCommandParser.cs` (puro, D3).
- `Persistence/Chat/` y `Persistence/Admin/`: sinks nuevos (D7), mismo patrón que `Persistence/Combat/`.
- `Persistence/Accounts/Account.cs`, `AccountRepository.cs`, `AuthService.cs`, `Net/Session.cs`: `IsAdmin` (D6).
- `World/WorldJoinRequest.cs`, `PlayerEntity.cs`, `Zone.cs`: `AccountId`/`IsAdmin` (D6).
- `World/GameWorld.cs`: `HandleChatSend` y los handlers de cada `ChatCommand`.

## 6. Contenido

Ninguno. Chat y administración no son datos de juego.

## 7. Cliente Godot

Sin arte. `NetClient` gana `SendChatSend`/`ChatMessageReceived`. `Ui/ChatScreen.cs` nuevo: una caja
de texto (oculta) y un log de las últimas líneas, tecla `Enter` para abrir/mandar — mismo criterio
minimalista que el resto de pantallas de `Ui/`.

## 8. Tests

`Server.Tests`: `ChatFilterTests`, `ChatCommandParserTests` (parseo puro), y en `WorldTests.cs` un
mensaje global que le llega a otro jugador, un susurro que no le llega a un tercero, y un comando
de admin rechazado para quien no lo es.

## 9. Verificación sin Godot

`tools/Epimeteo.WorldBot --chat`: dos bots, uno admin (se promociona por SQL entre el registro y la
verificación — no hay otra forma, D6 lo exige a propósito) y otro no. Mensaje global visible para
los dos, susurro que sólo le llega al destinatario, `/who` lista a los dos, comando de admin desde
la cuenta no-admin rechazado y desde la admin aplicado, con fila en `admin_action_log`.

## 10. Criterio de aceptación

1. `dotnet build` sin warnings, `dotnet test` en verde.
2. Un mensaje global le llega a todos los conectados; uno de zona, sólo a los de la misma zona.
3. Un susurro sólo le llega al destinatario; a un nombre inexistente, se rechaza.
4. `/who` lista a los conectados; `/help` lista los comandos.
5. Los comandos de admin sólo funcionan para cuentas con `is_admin`; los cuatro dejan fila en
   `admin_action_log`, y `/ban` además dejan a la cuenta baneada de verdad (no puede volver a
   entrar).
6. Cliente Godot compila sin warnings.

---

## 11. Resultado y hallazgos reales de la verificación E2E

Lo de arriba es el plan; esto es lo que pasó al ejecutarlo con
`tools/Epimeteo.WorldBot --chat-setup` / `--chat-verify` contra el servidor de producción real.

### Un fallo real de la herramienta, no del servidor: `Bot.CharacterName` no era el nombre de verdad

`Bot.CharacterName` es el nombre que se le pide al constructor — correcto sólo si el bot **crea**
el personaje (`CharCreate`, Fases 1-10, donde cada corrida siempre registraba una cuenta nueva).
Fase 11 es la primera en **reconectar** bots ya creados en una corrida anterior (`--chat-verify`
reutiliza las cuentas de `--chat-setup`, D5), y al reconectar el personaje ya existe: `CharSelect`
no vuelve a mandar el nombre, así que `CharacterName` se quedaba con el valor de mentira que se le
había pasado al constructor (`"no-hace-falta"`, mismo placeholder que usan `--progression-verify`/
`--farm-harvest` porque nunca antes hacía falta el nombre de verdad tras reconectar). Los comandos
que construían texto con ese nombre (`/give`, `/kick`) buscaban a alguien que no existía y se
rechazaban en silencio con `TargetNotFound` — y **`/teleport` parecía funcionar** por pura
coincidencia: todos los bots seguían quietos en el mismo punto de aparición desde que conectaron,
así que la distancia admin-objetivo ya era 0 antes de mandar el comando, con o sin él.

Arreglado añadiendo `Bot.Name` — el de `CharList` si reconecta, el de `CharacterName` si crea el
personaje él mismo — y usándolo en vez de `CharacterName` para construir cualquier comando que
necesite el nombre de verdad. Detalle que no había hecho falta en ninguna fase anterior porque
ninguna reconectaba con un bot para usarlo como *objetivo* con nombre, sólo como sujeto que vuelve
a jugar (`--progression-verify` no manda ningún `/comando` con su propio nombre dentro).

### Un choque real entre dos límites ya existentes, ninguno nuevo de esta fase

El cupo de login/registro por IP (`Epimeteo:LoginAttemptsPerMinute`, 5/minuto, Fase 2) y el
timeout de sesión inactiva (30 s, Fase 1) llevan reservados desde el principio, pero hasta ahora
nunca habían chocado: cada fase anterior conectaba pocos bots de golpe. `--chat-verify` conecta
cuatro (admin + tres objetivos) y, si el cupo de IP obliga a uno a esperar 65 s
(`AuthService.LoginAsync`, reintento con espera), los que ya habían conectado **no mandan nada**
mientras tanto —nada en el bot los pinga durante la propia espera del que reintenta— y el barrido
de inactividad los expulsa a los 30 s, bastante antes de que el rezagado termine. El síntoma fue
exactamente el mismo patrón engañoso que arriba: `/teleport` "funcionaba" (0,00 tiles, nadie se
había movido nunca) y el resto fallaba en cascada con `KickReason.Timeout`, no por nada que
tocara esta fase.

No es un fallo de ninguno de los dos límites —los dos siguen haciendo justo lo que tenían que
hacer— sino de la propia corrida de verificación pidiendo demasiadas conexiones nuevas demasiado
rápido después de `--chat-setup`. Se resolvió reduciendo `--chat-verify` a **una sola cuenta
nueva** (la de `/ban`; los otros tres objetivos reconectan con las cuentas que ya creó
`--chat-setup`, registrada la primera y antes de que nadie más conecte) y, cuando aun así se
volvió a topar con el cupo, simplemente esperando a que la ventana de un minuto se vaciara del
todo antes de reintentar — ningún cambio de código adicional hacía falta una vez reducida la
presión de registros.

### Verificación real ejecutada

`--chat-setup`, **11/11 comprobaciones en verde**:

1. ✅ Un mensaje global le llega a otro jugador, con el nombre de quien lo mandó.
2. ✅ Un mensaje de zona también le llega a quien está en la misma zona.
3. ✅ Un susurro sólo le llega al destinatario, con eco a quien lo manda, y no a un tercero.
4. ✅ Susurrar a un nombre que no existe se rechaza (`chat.TargetNotFound`).
5. ✅ `/who` lista a los conectados; `/help` lista los comandos.
6. ✅ Un comando de admin (`/kick`) sin serlo se rechaza (`chat.NotAuthorized`) y no expulsa a nadie.

`/kick` promovido a admin por SQL (D6, ninguna cuenta se autopromociona) y `--chat-verify`,
**5/5 comprobaciones en verde**:

7. ✅ `/teleport` mueve al admin junto al objetivo.
8. ✅ `/give` mete el ítem de verdad en la bolsa del objetivo (se apiló sobre las que ya traía del
   kit inicial: de 2 a 4 — prueba mejor que el ítem llegó de verdad que si el hueco hubiera estado
   vacío).
9. ✅ `/kick` expulsa al objetivo sin tocar su cuenta.
10. ✅ `/ban` expulsa al objetivo y deja la cuenta baneada de verdad: no puede volver a entrar.

En `psql`: las cuatro filas de `admin_action_log` (`kick`, `ban`, `teleport`, `give`) con el
admin, el objetivo, el motivo y los detalles correctos; `chat_log` con las líneas de la fase 1;
`accounts.status = 2` y `banned_until`/`ban_reason` reales para la cuenta baneada.

427 tests en verde (155 `Shared` + 272 `Server`). Cliente Godot: compila sin warnings, con caja de
chat (`Enter` para abrir/mandar, `T` para alternar global/zona) y log de las últimas líneas en el
HUD; como en las Fases 4–10, **no se ha ejecutado** — sigue sin haber Godot en este servidor
headless.
