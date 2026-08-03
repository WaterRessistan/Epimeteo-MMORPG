# FASE 02 — Persistencia y autenticación

**Modelo:** Sonnet · **Estado:** 📋 planificada

> Diseño ya cerrado en `docs/02-esquema-bd.md` (accounts, account_sessions, login_attempts,
> characters, item_instances) y `docs/01-protocolo.md` (`Login`/`Register`/`AuthResult`). Esta
> fase implementa: la migración `0001_init.sql`, el runner de DbUp, el hash Argon2id, los dos
> opcodes de auth con sus repositorios Dapper, y las pantallas de Godot que los usan.

## 1. Objetivo

Registro y login reales contra PostgreSQL. Criterio de aceptación: desde el cliente Godot,
crear una cuenta nueva, cerrar el cliente, volver a abrirlo y entrar con las mismas credenciales
— la sesión pasa a `Authenticated` y el servidor lo puede comprobar en `/status` o en logs.

## 2. Postgres local (decidido con el usuario)

**Nativo por `apt`, no Docker.** Producción ya es Ubuntu 24.04 + systemd sin Docker en el stack
(`CLAUDE.md §2`); ensayar en local el mismo camino evita sorpresas en la Fase 5. Además el daemon
de Docker corre como root y pertenecer al grupo `docker` es equivalente a root sin contraseña —
para una única BD de desarrollo no compensa la pieza extra.

Esta sesión no tiene `sudo` con TTY, así que estos comandos los lanza el usuario (con el prefijo
`!` si quiere verlos en la conversación) **antes** de que se implemente nada que dependa de ellos:

```bash
sudo apt install -y postgresql-16
sudo systemctl enable --now postgresql
sudo -u postgres createuser -P epimeteo        # pide contraseña de desarrollo
sudo -u postgres createdb -O epimeteo epimeteo
```

Verificación: `psql "postgresql://epimeteo:<password>@localhost:5432/epimeteo" -c '\conninfo'`.

El paquete de Ubuntu ya escucha sólo en `localhost` por defecto (`listen_addresses` no toca).
No se cambia `pg_hba.conf`: `scram-sha-256` con contraseña es suficiente para desarrollo en la
misma máquina.

La cadena de conexión vive en `server/Epimeteo.Server/appsettings.Development.json`
(ya en `.gitignore`), sección `ConnectionStrings:Epimeteo`. `appsettings.json` (versionado) sólo
lleva un valor de ejemplo comentado, nunca una contraseña real.

`pgdata/` se queda en `.gitignore` sin usarse — era una anticipación de la Fase 0 pensando en
Docker; no estorba y puede servir si algún día un pipeline de CI monta Postgres en contenedor.

## 3. Migraciones con DbUp

- Paquete `dbup-postgresql` en el servidor.
- Los ficheros `.sql` viven en `db/migrations/NNNN_descripcion.sql` (fuente de verdad, git).
  Se referencian como `<EmbeddedResource>` desde `Epimeteo.Server.csproj` (link, no copia) para
  que el ejecutable publicado no dependa de encontrar la carpeta en disco tras el despliegue.
- `DbUp.EnsureDatabase.For.PostgresqlDatabase(...)` + `DeployChanges.To.PostgresqlDatabase(...)
  .WithScriptsEmbeddedInAssembly(...)`. Se ejecuta **una vez, al arrancar**, antes de
  `app.Run()`; si falla, el servidor no arranca (falla ruidoso, no silencioso).
- Tabla de control `schema_versions_journal` (la que crea DbUp por defecto). Nunca se edita una
  migración ya aplicada: la siguiente corrección es `0002_...sql`.

### `db/migrations/0001_init.sql`

Traslado directo de `docs/02-esquema-bd.md` con cinco tablas — las que pide el roadmap para esta
fase, aunque `characters` e `item_instances` no tengan todavía código que las use (las llena la
Fase 3 y la Fase 6; crear el esquema ahora evita una migración `0002` de puro DDL sin motivo):

- `accounts` (+ extensiones `citext`, `pgcrypto`)
- `account_sessions`
- `login_attempts`
- `characters`
- `item_instances`

Sin cambios de diseño respecto al documento — se copian los `CREATE TABLE`/`INDEX` tal cual.

## 4. Hash de contraseñas — Argon2id

- Paquete `Konscious.Security.Cryptography.Argon2`.
- `PasswordHasher` en `server/Epimeteo.Server/Persistence/Accounts/PasswordHasher.cs`:
  - `Hash(string password) → string` codifica en un único campo de texto
    `argon2id$v=19$m=<KB>,t=<iters>,p=<par>$<saltB64>$<hashB64>` (formato tipo PHC, autocontenido:
    permite subir los parámetros en el futuro sin invalidar hashes viejos — se leen del propio
    string, no de una constante).
  - `Verify(string password, string encoded) → bool`.
  - Parámetros de partida (WSL2, sin GPU dedicada que atacar): memoria 19 MB, 2 iteraciones,
    paralelismo 1 — el mínimo recomendado por OWASP para Argon2id; se documentan como
    ajustables, no se inventan más altos sin medir el coste real en el hardware de producción.
  - `password_ver` en `accounts` guarda la versión de parámetros para poder migrar hashes en
    caliente el día que se suban (rehash perezoso en el próximo login exitoso).
- Test unitario: `Hash` + `Verify` con la contraseña correcta → true; con otra → false;
  dos `Hash()` de la misma contraseña dan strings distintos (sal aleatoria).

## 5. Mensajes de red (ya reservados en el protocolo, faltan por tipar)

Nuevos ficheros en `shared/Epimeteo.Shared/Net/Messages/`, mismas convenciones que la Fase 1
(`[MessagePackObject]`, `[Key(n)]`, sin inicializadores en propiedades `init` — regla de la
Fase 1 §3.1):

| Fichero | Opcode | Campos |
|---|---|---|
| `C2SLogin.cs` | `0x0002 Login` | `Username string`, `Password string` |
| `C2SRegister.cs` | `0x0003 Register` | `Username string`, `Email string?`, `Password string` |
| `S2CAuthResult.cs` | `0x8002 AuthResult` | `Ok bool`, `Code ResultCode`, `AccountId long`, `SessionToken string?` |

`Password` viaja en claro **dentro del WebSocket cifrado (`wss://`)** — nunca se hashea en
cliente: hashear en cliente convertiría el hash en la contraseña real de cara al servidor
(pass-the-hash) y además fijaría para siempre los parámetros de Argon2 en el protocolo.

## 6. Validación y límites (antes de tocar la BD)

- `Username`: 3–20, mismo `CHECK` que la tabla (`username_format`); además sólo
  `[a-zA-Z0-9_]` en servidor — el `CHECK` de la BD no basta porque `citext` no impone charset.
- `Password`: longitud 8–72 (72 es el límite práctico de Argon2id razonable para no permitir
  que un cliente mande megabytes de "contraseña" y agote CPU en el hash). Fuera de rango →
  `ResultCode.NameInvalid` para username, y un nuevo... **no**: se reutiliza
  `InvalidCredentials` para password inválida en login, y para registro con password fuera de
  rango se usa `NameInvalid` no encaja — se añade `ResultCode.PasswordInvalid = 105` (hueco libre
  en el bloque 100–199, mismo bloque que `NameInvalid`). Es la única adición al enum compartido
  en esta fase.
- `Email`: opcional; si viene, formato mínimo (`Contains('@')`, longitud) — sin regex compleja,
  la única validación real de un email es enviarle un correo, y eso no está en el alcance.

## 7. Rate limit de login — persistido, no en memoria

`docs/01-protocolo.md`: **5 por minuto por IP**, no por sesión. El `SessionRateLimiter` actual
(Fase 1) es en memoria y por sesión — no sirve aquí porque un atacante reconecta y resetea su
cupo. Se implementa como consulta a `login_attempts` **antes** de intentar `Login`/`Register`:

```sql
SELECT count(*) FROM login_attempts
 WHERE ip = @ip AND attempted_at > now() - interval '1 minute';
```

Si ≥ 5 → `ResultCode.RateLimited` sin tocar `accounts`, y se registra igualmente el intento
(`success = false`) para que el propio spam cuente contra el límite. Cada intento de `Login`
(éxito o fracaso) inserta una fila; `Register` con éxito no cuenta como intento de login (crea
cuenta, no autentica con contraseña ajena) pero sí comparte el límite de 5/minuto por IP como
familia `Auth` a nivel de `OpcodeTable` (eso ya lo hace el rate limiter de sesión de la Fase 1;
aquí se añade el segundo nivel, persistente y por IP, específico de auth).

## 8. Tokens de sesión

- Al autenticar con éxito: generar 32 bytes aleatorios (`RandomNumberGenerator.GetBytes`),
  mandar el token **en claro** al cliente en `S2CAuthResult.SessionToken`, y guardar sólo su
  `SHA-256` en `account_sessions.token_hash` — igual que un password, el servidor nunca guarda
  el secreto reutilizable en claro.
- `expires_at`: 7 días. No hay endpoint de "reconectar con token" en esta fase (eso es más
  bien pantalla de "recordar sesión" en Godot, fuera de alcance); el campo se llena para que el
  esquema esté listo, pero el único camino de esta fase es usuario+contraseña. Se deja anotado
  como pendiente explícito en "Fuera de alcance" — no se implementa un opcode nuevo para no
  inventar protocolo que `docs/01` no declara.
- `Session.AccountId` (nuevo campo, como `ClientBuild` en la Fase 1) guarda la cuenta tras
  autenticar; lo usa la Fase 3 para listar personajes.

## 9. Servidor — piezas nuevas

```
server/Epimeteo.Server/Persistence/
  NpgsqlConnectionFactory.cs      # abre IDbConnection desde la cadena de ConnectionStrings:Epimeteo
  MigrationRunner.cs              # DbUp contra scripts embebidos, llamado desde Program.cs
  Accounts/
    PasswordHasher.cs
    AccountRepository.cs          # Dapper: FindByUsername, Create, TouchLastLogin
    LoginAttemptRepository.cs     # Dapper: CountRecent(ip), Record(ip, username, success)
    SessionTokenService.cs        # Issue(accountId) -> token en claro; hash y persistencia
```

`SessionMessageHandler` gana dos casos (`Login`, `Register`), resueltos **en el hilo de red**
como `Hello`/`Ping` (Fase 1 §3.4): tocan Postgres, no el tick de mundo, y el bucle de tick
síncrono de una zona no debe esperar E/S de BD (regla no negociable de `CLAUDE.md §4`). El
`await` de Dapper/Npgsql cruza de forma async normal en el hilo de red — no hay violación de la
regla, que es específicamente "nunca `await` de BD dentro del tick".

`ServerOptions` no lleva la cadena de conexión (es secreto, no configuración de red); se lee por
separado de `ConnectionStrings:Epimeteo` con el builder estándar de `IConfiguration`.

## 10. Cliente Godot

- `scenes/Login.tscn` + `scripts/Ui/LoginScreen.cs`: dos campos (usuario, contraseña), botón
  entrar, enlace a "crear cuenta".
- `scenes/Register.tscn` + `scripts/Ui/RegisterScreen.cs`: usuario, email (opcional), contraseña,
  confirmar contraseña (validación **sólo de UX** en cliente — el servidor es quien decide de
  verdad).
- Transición: `Connect` (Fase 1) → al `HelloAck`, pasa a `Login` en vez de quedarse mostrando RTT.
- Mapeo de `ResultCode` → texto en español, tabla local en el cliente (el servidor nunca manda
  texto, `docs/01-protocolo.md §Códigos de error`).
- Guarda el `SessionToken` recibido en memoria (no en disco todavía — persistirlo en disco abre
  la pregunta de dónde y cómo protegerlo, y esta fase no lo necesita porque no hay reconexión).

## 11. Tests

En `tests/Epimeteo.Shared.Tests/`: ninguno nuevo relevante (los mensajes son records de datos,
ya cubiertos por el patrón de `FrameCodecTests`).

Nuevo proyecto `tests/Epimeteo.Server.Tests/` (primera vez que el servidor tiene tests propios):
- `PasswordHasherTests`: hash/verify, sal distinta cada vez, verify falla con otra contraseña.
- `LoginAttemptRepositoryTests` y `AccountRepositoryTests`: **contra Postgres real**, no mocks
  — es la única forma honesta de probar `CHECK`, índices únicos y la ventana de rate limit; se
  ejecutan sólo si `ConnectionStrings:Epimeteo` está configurada (se saltan, no fallan, en un
  entorno sin Postgres) y limpian sus filas al terminar.

## 12. Criterio de aceptación

1. `sudo -u postgres ...` ya ejecutado por el usuario; `psql` conecta.
2. `dotnet run --project server/Epimeteo.Server` aplica `0001_init.sql` solo, log "N migraciones
   aplicadas" (o "0 pendientes" en el segundo arranque).
3. Cliente Godot: registrar cuenta nueva → pantalla pasa a mostrar personajes (placeholder de
   Fase 3, basta con un mensaje "autenticado, accountId N").
4. Cerrar cliente, reabrir, login con las mismas credenciales → mismo resultado.
5. Login con contraseña incorrecta → `InvalidCredentials`, sin cerrar la conexión.
6. Registro con usuario ya existente → `AccountAlreadyExists`.
7. 6 logins fallidos seguidos desde el mismo cliente en menos de un minuto → el 6º da
   `RateLimited` sin ni siquiera comprobar la contraseña.
8. `dotnet test` en verde (los tests contra Postgres real corren porque hay Postgres local).

## 13. Fuera de alcance

Reconexión con token guardado, 2FA (`totp_secret` ya está en el esquema, sin lógica), verificación
de email, "olvidé mi contraseña", ban/suspensión desde admin (columna `status` existe, sin UI de
gestión), selección de personajes (Fase 3), TLS real (Fase 5 — en local sigue siendo `ws://`
loopback igual que la Fase 1).
