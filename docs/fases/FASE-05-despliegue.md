# FASE 05 — Despliegue mínimo

> Modelo: **Sonnet** (CLAUDE.md §6). Es mecanografía sobre un diseño ya cerrado en `docs/00 §5`.
>
> Prerrequisito leído: `docs/STATUS.md` (Fase 4 completa, cliente Godot sin probar por falta de
> entorno gráfico), `docs/00 §2 y §5`.

---

## 0. Lo que cambia respecto a lo que dice `docs/00 §5`

`docs/00 §5` dejaba pendiente de confirmar quién ocupa el 443. Diagnóstico de esta sesión en el
propio servidor de producción:

```
$ sudo ss -lntp | grep -E ':(80|443|8080|8443)\b'
nginx    → 80, 443   (certbot ya gestiona el único dominio actual, waterressistan.duckdns.org)
uvicorn  → 8080      (dashboard tmux, TLS propio, no pasa por nginx, no accesible desde fuera)
node     → 8443      (otro servicio, TLS propio)
```

**Escenario confirmado: nginx tiene el 443. Es el preferido según la tabla de `docs/00 §5`.**

Dos cosas que la sesión anterior no sabía y que simplifican la fase:

1. **DuckDNS es wildcard.** `dig` confirma que `cualquier-cosa.waterressistan.duckdns.org`
   resuelve a la misma IP que el dominio base. No hace falta un dominio nuevo ni tocar DNS:
   `epimeteo.waterressistan.duckdns.org` ya apunta aquí.
2. **Este servidor ya es el de producción.** Las Fases 1–4 se verificaron en esta misma máquina
   (`docs/STATUS.md § Entorno`), con Postgres real y el rol `epimeteo` ya creado. Fase 5 no
   provisiona un servidor nuevo: convierte lo que hasta ahora se lanzaba a mano con
   `dotnet run` en un servicio systemd de verdad, con su propio dominio público.

`iptables -L INPUT` confirma que el 443 ya está abierto al exterior (regla explícita `ACCEPT
tcp dpt:443`) y que no hay más puertos que abrir: el juego sigue en loopback, sale por nginx.
`ufw` no está activo en esta máquina. **No hace falta tocar el firewall.**

---

## 1. Objetivo

Que alguien en una red 4G, sin VPN ni acceso al servidor, entre a
`wss://epimeteo.waterressistan.duckdns.org/ws` y juegue. Que el proceso sobreviva a un reinicio
del servidor y a un `git pull` + redeploy sin intervención manual más allá de un script.

**Fuera de alcance:** HA, múltiples instancias, Redis (CLAUDE.md §2: sólo si se llega a la
Fase 14), CDN para el cliente Godot exportado (el cliente se sigue abriendo desde el editor o un
build local; empaquetar y distribuir el `.exe`/`.pck` no es parte de esta fase).

---

## 2. Las cuatro piezas

### D1 — nginx: fichero propio, no tocar `default`

`/etc/nginx/sites-available/default` está anotado `# managed by Certbot` y sirve el dominio que
ya está en producción (`waterressistan.duckdns.org`, usado por otra cosa). Meter la config del
juego ahí dentro sería editar a mano un fichero que Certbot reescribe y arriesgar el dominio que
ya funciona.

En vez de eso: `/etc/nginx/sites-available/epimeteo`, symlink propio en `sites-enabled/`,
`server_name epimeteo.waterressistan.duckdns.org` exclusivo. Certbot, apuntado a este fichero con
`--nginx -d epimeteo.waterressistan.duckdns.org`, gestiona su propio bloque `listen 443 ssl` sin
tocar el certificado del otro dominio.

```nginx
server {
    listen 80;
    listen [::]:80;
    server_name epimeteo.waterressistan.duckdns.org;
    location / { return 301 https://$host$request_uri; }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    server_name epimeteo.waterressistan.duckdns.org;

    # certbot rellena ssl_certificate / ssl_certificate_key / include options-ssl-nginx.conf

    location /ws {
        proxy_pass http://127.0.0.1:5100/ws;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_read_timeout 3600s;   # si no, nginx corta la partida al minuto (docs/00 §5)
    }

    location / {
        proxy_pass http://127.0.0.1:5101;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
```

### D2 — el servidor tiene que confiar en `X-Forwarded-For`

Hallazgo de esta sesión: `Program.cs` usa `context.Connection.RemoteIpAddress` para el rate limit
de login (`AuthService`, `docs/01-protocolo.md § Ritmos`, 5/min por IP). Detrás de nginx esa
propiedad **siempre** sería `127.0.0.1` — el límite dejaría de proteger a nadie, todo el mundo
compartiría el mismo cupo.

Arreglo: middleware `ForwardedHeaders` de ASP.NET Core, con `KnownProxies` restringido a
`127.0.0.1`/`::1` (nginx corre en la misma máquina; **no** se acepta la cabecera de cualquiera,
sólo la que venga del propio loopback — si no, cualquier cliente podría falsificar su IP mandando
`X-Forwarded-For` directamente al puerto de Kestrel... salvo que ese puerto ya es sólo loopback,
así que el riesgo real es nulo, pero restringir `KnownProxies` es gratis y es la práctica
correcta). Se activa **antes** de `UseWebSockets`.

### D3 — un usuario dedicado, `/opt/epimeteo`, systemd

Exactamente lo que ya dice `CLAUDE.md §2` y `docs/00 §5`, con el estilo de las unidades que ya
existen en esta máquina (`nucleo-dashboard.service`) como referencia de formato, no de permisos:
esas corren como `ubuntu` porque son scripts de un solo usuario; el juego acepta conexiones de
desconocidos por WebSocket, así que le toca aislamiento de verdad.

- Usuario de sistema `epimeteo`, sin shell (`--system --no-create-home --shell /usr/sbin/nologin`).
- `/opt/epimeteo/app/` — salida de `dotnet publish`, propiedad de `epimeteo:epimeteo`.
- `/opt/epimeteo/content/` — copia de `content/`, la misma idea que `ClientContent` en el cliente
  Godot (Fase 4): el publish no lleva el repositorio entero, así que hay que decidir dónde vive el
  contenido fuera de él.
- `/opt/epimeteo/appsettings.Production.json` — cadena de conexión real, fuera de git, sólo
  legible por `epimeteo` (`chmod 600`).
- `epimeteo.service`: `Restart=always`, `ProtectSystem=strict`, `ProtectHome=true`,
  `ReadWritePaths=/opt/epimeteo/logs`, `NoNewPrivileges=true`, `WorkingDirectory=/opt/epimeteo/app`,
  `Environment=ASPNETCORE_ENVIRONMENT=Production`, `After=network-online.target postgresql.service`.

### D4 — `ContentPaths` no sirve para esto

`ContentPaths.ResolveContentRoot()` (Fase 3) sube desde `AppContext.BaseDirectory` hasta
encontrar `Epimeteo.sln`. En `/opt/epimeteo/app/` no hay ningún `.sln`: reventaría al arrancar.
Mismo problema que se documentó al escribir `ClientContent` en la Fase 4, y misma solución: probar
primero una carpeta `content/` **junto al binario** (lo que deja el script de publicación) y sólo
si no está, subir buscando `Epimeteo.sln` (para que `dotnet run` en el repo siga funcionando sin
cambios, en desarrollo y en los tests).

---

## 3. Entregables

| Fichero | Qué hace |
|---|---|
| `deploy/nginx-epimeteo.conf` | El bloque de arriba, versionado. Se copia (no se enlaza) a `/etc/nginx/sites-available/epimeteo`: nginx no lee de fuera de `/etc`. |
| `deploy/epimeteo.service` | La unidad systemd de D3, versionada. Se copia a `/etc/systemd/system/`. |
| `deploy/publish.sh` | `dotnet publish` (framework-dependent: ya hay `dotnet` en el servidor, y self-contained sólo añade tamaño sin resolver nada aquí) + copia `content/` + `rsync --delete` a `/opt/epimeteo/app` y `/opt/epimeteo/content` + `systemctl restart epimeteo`. Falla ruidoso si `dotnet build` o `dotnet test` no están en verde antes de publicar. |
| `deploy/backup-postgres.sh` + `deploy/epimeteo-backup.{service,timer}` | `pg_dump` a `/opt/epimeteo/backups/epimeteo-YYYY-MM-DD.sql.gz`, purga lo de hace más de 14 días. El timer lo dispara una vez al día; nada nuevo que recordar ejecutar a mano. |
| `server/Epimeteo.Server/appsettings.Production.json.example` | Plantilla, igual que la de Development (Fase 2). El real no se commitea. |
| Cambios en `Program.cs` | `ForwardedHeaders` (D2) y el `ContentPaths` de dos pasos (D4). |
| `docs/00-arquitectura.md §5` | Se borra el aviso "pendiente de confirmar" y se documenta el escenario real. |

---

## 4. Orden de trabajo

1. `ForwardedHeaders` en `Program.cs` + ajuste de `ContentPaths`. Se puede probar en local (`dotnet
   run` con `ASPNETCORE_ENVIRONMENT=Production` contra un `content/` copiado a mano) antes de tocar
   nada de producción.
2. `deploy/nginx-epimeteo.conf`, copiarlo, `nginx -t`, `systemctl reload nginx` — todavía sin
   certificado, así que sólo el bloque `:80` sirve; sirve para confirmar que el DNS y el proxy
   básico funcionan antes de pedir TLS.
3. `certbot --nginx -d epimeteo.waterressistan.duckdns.org` (certificado propio, no toca el de
   `waterressistan.duckdns.org`). Confirmar renovación automática con `certbot renew --dry-run`.
4. Usuario `epimeteo`, `/opt/epimeteo/`, `appsettings.Production.json` real (con la contraseña de
   Postgres ya existente del rol `epimeteo` — no hace falta una BD nueva, esta máquina ya es la de
   producción).
5. `deploy/epimeteo.service`, `deploy/publish.sh`, primer publish y arranque.
6. `deploy/backup-postgres.sh` + timer. Verificar un `pg_dump` y una restauración de prueba en una
   BD temporal (no contra `epimeteo`).
7. Verificación externa: apagar cualquier acceso previo por loopback y conectarse **desde fuera**
   (4G del móvil, o `curl` desde otra máquina) a `wss://epimeteo.waterressistan.duckdns.org/ws`.

---

## 5. Validación y errores

| Situación | Respuesta |
|---|---|
| `nginx -t` falla tras editar la config | No se recarga; se corrige antes de `reload`. Nunca `restart` a ciegas: tumbaría también el dominio que ya funciona. |
| Certbot no puede validar el dominio | Confirmar que el `:80` del paso 2 ya responde (el reto ACME HTTP-01 pasa por ahí) antes de reintentar. |
| El publish falla `dotnet test` | El script no publica. Nunca se sube código sin tests en verde a producción. |
| `content/` no está en `/opt/epimeteo/content` | `ContentPaths` no encuentra nada válido → el servidor no arranca (falla ruidoso, igual que un mapa corrupto en la Fase 4), no un 500 silencioso. |
| El servicio no arranca tras `systemctl start` | `journalctl -u epimeteo -n 50`; con `ProtectSystem=strict` un permiso mal puesto en `/opt/epimeteo` es la causa más probable. |

---

## 6. Criterio de aceptación

1. `sudo nginx -t` en verde y `systemctl status epimeteo` activo tras un `reboot` (o al menos tras
   `systemctl daemon-reexec` + reinicio del servicio, si no se puede reiniciar la máquina entera).
2. `certbot certificates` lista `epimeteo.waterressistan.duckdns.org` con fecha de caducidad y
   `certbot renew --dry-run` en verde.
3. Desde una red externa (4G, no la red local del servidor): `wscat` o el `WorldBot` apuntando a
   `wss://epimeteo.waterressistan.duckdns.org/ws` completan el handshake y entran al mundo.
4. `deploy/publish.sh` ejecutado dos veces seguidas: la segunda no rompe nada (idempotente), y el
   servicio queda arriba con el código nuevo (`/version` cambia si se tocó algo).
5. Un `pg_dump` de `deploy/backup-postgres.sh` se restaura sin error en una base de datos vacía de
   prueba.
6. `docs/00-arquitectura.md §5` ya no dice "pendiente de confirmar".

---

## 7. Riesgos

| Riesgo | Mitigación |
|---|---|
| Romper `waterressistan.duckdns.org`, que ya está en producción para otra cosa | Fichero de nginx propio (D1), nunca editar `default`; `nginx -t` antes de cada `reload`. |
| El rate limit de login queda inútil detrás del proxy | D2, con test manual: dos IPs distintas mandando `X-Forwarded-For` falso directamente al puerto de Kestrel (que es loopback, así que ya no son alcanzables desde fuera) para confirmar que sin pasar por nginx no se puede falsificar nada. |
| `ProtectSystem=strict` bloquea algo que el proceso necesita escribir (logs, la cola de guardado) | `ReadWritePaths` explícito sólo para lo que hace falta; se descubre rápido en `journalctl` si falta alguno. |
| Un publish a medias deja el servicio caído | `rsync` a un directorio y `restart` sólo si el build/test previos pasaron; el `Restart=always` de systemd no ayuda si el binario está corrupto a medio copiar. |
