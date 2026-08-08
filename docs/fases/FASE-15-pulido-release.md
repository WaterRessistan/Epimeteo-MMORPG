# FASE 15 — Pulido y release

> Modelo: **Sonnet** (CLAUDE.md §6): implementación sobre reglas ya escritas.

## 1. Objetivo, y qué parte de la fase toca esta sesión

El roadmap (`docs/03`) mete en una sola fase tres cosas de naturaleza muy distinta:

| Lo que pide `docs/03` | Naturaleza | ¿Esta sesión? |
|---|---|---|
| Audio, transiciones, menú de opciones, remapeo de teclas, pantalla completa | Cliente Godot, UI interactiva | **No** |
| Builds de distribución Windows/Linux, primera versión etiquetada | Exportación Godot con *export templates* | **No** |
| Launcher/parcheador contra `/files/` con manifiesto de hashes | Servidor + herramienta de consola | **Sí** |

Este servidor **no tiene Godot instalado y nunca ha tenido entorno gráfico**
(`docs/STATUS.md`, nota repetida desde la Fase 4). El menú de opciones, el remapeo de teclas y la
pantalla completa son interacción de UI que no se puede verificar sin ver la pantalla — escribir
ese código a ciegas y marcarlo "hecho" sería peor que no tocarlo. El audio, además, choca con la
política de assets de CLAUDE.md §5 ("nunca generes ni descargues assets sin que se pida
explícitamente"): no hay clips que reproducir todavía. Las builds de distribución necesitan
*export templates* de Godot, que tampoco están instalados, y firmar/empaquetar un `.exe` sin poder
ejecutarlo para comprobar que arranca es trabajo a ciegas del mismo tipo.

El launcher/parcheador es distinto: es servidor HTTP + una herramienta de consola, exactamente el
tipo de trabajo que sí se puede escribir y verificar end-to-end en este entorno, igual que todas
las fases anteriores. Se hace esta sesión; el resto se anota en `docs/STATUS.md` para cuando haya
una máquina con Godot.

**Decisión de alcance, tomada con el usuario antes de escribir este plan:** dividir la fase en
vez de forzarlo todo en una sesión sin poder verificar media fase.

## 2. Decisiones de diseño

### D1 — Los ficheros de parche viven en `client-build/`, no en `content/` ni en `client/`

`content/*.json` es dato de juego versionado en git, resuelto por clave (CLAUDE.md §3) — otro
concepto. `client/` es código fuente. Lo que el launcher distribuye es la **salida de una build**
del cliente: binarios, `.pck`, lo que Godot exporte. Eso no se versiona en git (pesa y cambia en
cada build), así que va en `client-build/` en la raíz.

No se llama `release/`: el `.gitignore` ya tenía un patrón `[Rr]elease/` genérico heredado de la
plantilla de .NET (pensado para `bin/Release/`, `obj/Release/`), y una carpeta nueva con ese
nombre habría caído en él sin darse cuenta — el propio `README.md` marcador habría quedado sin
versionar. Con `client-build/` se evita la colisión en vez de reescribir una regla que cumple otro
propósito; el `.gitignore` gana una entrada dedicada (`/client-build/*` con `!/client-build/README.md`)
para que el contenido real de una build no se versione pero el marcador sí.

Ahora mismo no existe ninguna build real de Godot. `client-build/` se deja con un único fichero de
marcador (`README.md` explicando que aún no hay build) para que el mecanismo tenga algo real que
servir y verificar de punta a punta sin fabricar "ficheros de juego" falsos.

### D2 — Manifiesto: JSON con SHA-256 y tamaño por fichero, generado por una herramienta nueva

`tools/Epimeteo.ReleaseTool` (mismo patrón que `Epimeteo.ContentValidator`: consola, un único
propósito) recorre `client-build/` recursivamente, calcula SHA-256 y tamaño de cada fichero, y escribe
`client-build/manifest.json`:

```json
{
  "generatedAtUtc": "2026-08-08T12:00:00Z",
  "files": [
    { "path": "README.md", "sha256": "…", "size": 123 }
  ]
}
```

SHA-256 y no MD5: no es una necesidad criptográfica (todo viaja por HTTPS), pero es el estándar
actual, .NET lo trae de fábrica y el coste de calcularlo sobre unos pocos cientos de MB de build
es insignificante frente a la propia descarga. `manifest.json` se genera dentro de `client-build/` y se
sirve como un fichero más: el launcher lo pide por la misma ruta que todo lo demás.

Rutas con `\` (Windows) se normalizan a `/` al generar el manifiesto, para que el mismo fichero
sirva a un launcher corriendo en Windows o en Linux sin traducir nada en el cliente.

### D3 — El servidor sirve `client-build/` bajo `/files/`, público, sin token

Igual que `/version`: un jugador que todavía no tiene el juego instalado no puede autenticarse
para descargarlo. `/files/` se añade a la lista blanca de rutas del puerto HTTP
(`Program.cs`, el mismo `app.Use` que ya limita el puerto 5101 a `/version`, `/status` y
`/metrics` desde la Fase 13) y **no** entra en el middleware de autenticación por token, que sigue
protegiendo sólo `/status` y `/metrics`.

Path traversal: la ruta pedida (`/files/{**path}`) es entrada no confiable y se resuelve con
`Path.GetFullPath` comprobando que el resultado sigue dentro de `client-build/` antes de abrir nada
— igual que cualquier otro límite de esta fase, se valida en servidor, no se confía en que el
cliente pida rutas razonables (CLAUDE.md, reglas de seguridad no negociables).

### D4 — El parcheador es una herramienta de consola nueva, no parte del cliente Godot

`tools/Epimeteo.Launcher`: dado un `--url` (por defecto la URL pública) y un `--dir` (directorio
local de destino), descarga `manifest.json`, compara SHA-256 local contra el manifiesto:

- Fichero ausente o con hash distinto → se descarga a un fichero temporal, se recalcula el hash
  de lo descargado y sólo si coincide se mueve (`File.Move` con reemplazo) al destino final. Un
  hash que no coincide tras la descarga no se acepta nunca — corrupción a medio camino no debe
  dejar un fichero "casi bueno" en su sitio.
- Fichero local que no está en el manifiesto → se borra. Un parcheador de verdad limpia lo viejo;
  si sólo añadiera, cada build dejaría basura de la anterior acumulándose para siempre.
- Fichero con hash igual → no se toca.

Se queda como herramienta de consola, no como parte de `client/`: no hay entorno gráfico donde
darle una ventana, y la lógica de comparar-y-descontinuar es igual de válida algún día detrás de
una UI que ahora en un CLI. Cuando exista una build de Godot real, un launcher gráfico puede
llamar al mismo flujo o reimplementarlo en GDScript/C# de cliente — decisión de esa sesión futura,
no de ésta.

### D5 — nginx: `/files/` se añade a la lista blanca, junto a `/version`

Después de la Fase 13, `location = /version` es la única ruta pública del puerto 5101; el resto
devuelve 404. `/files/` necesita ser un prefijo, no una coincidencia exacta (`manifest.json` y
cualquier fichero dentro), así que se añade como `location /files/ { proxy_pass … }` — pero
`/status` y `/metrics` siguen sin tener regla en nginx (siguen cayendo en el `location / { return
404; }` final), así que el hallazgo de seguridad de la Fase 13 no se reabre.

## 3. Ficheros

| Fichero | Qué |
|---|---|
| `client-build/README.md` | Marcador: no hay build de Godot todavía. |
| `tools/Epimeteo.ReleaseTool/*.csproj`, `Program.cs`, `Manifest.cs` | Genera `client-build/manifest.json`. Dueño del tipo `Manifest`. |
| `tools/Epimeteo.Launcher/*.csproj`, `Program.cs` | Descarga/borra contra el manifiesto; referencia el proyecto `Epimeteo.ReleaseTool` para reusar `Manifest` en vez de duplicarlo (mismo criterio que `Epimeteo.ContentValidator` referenciando `Epimeteo.Server`). |
| `server/Epimeteo.Server/Program.cs` | `/files/{**path}` en la lista blanca del puerto HTTP; sirve desde `client-build/`. |
| `server/Epimeteo.Server/Files/ReleasePaths.cs` | Localiza `client-build/`, mismo patrón de dos estrategias que `Content/ContentPaths.cs` — sin campo nuevo en `ServerOptions`, coherente con cómo ya se localiza `content/`. |
| `server/Epimeteo.Server/Files/SafeFileResolver.cs` | La comprobación de traversal, pura y testeable aparte del endpoint. |
| `deploy/nginx-epimeteo.conf` | `location /files/` nueva. |
| `tests/Epimeteo.Server.Tests/SafeFileResolverTests.cs` | Traversal rechazado en sus dos capas, fichero real resuelto, inexistente → null. Prueba `SafeFileResolver` directamente, no el endpoint completo — igual que `MetricsRegistryTests` prueba clases puras en vez de levantar todo el `WebApplication`, que además necesitaría Postgres. La lista blanca del puerto HTTP (`/files` en `Program.cs`) se comprueba con `curl` de punta a punta, como el resto de la fase. |

## 4. Fuera de alcance a propósito, de nuevo

- Delta-patching (descargar sólo los bytes que cambiaron dentro de un fichero grande). Con
  builds del tamaño de un juego 2D en pixel art, descargar el fichero entero cuando cambia es
  aceptable; delta-patching es complejidad real para un problema que no existe todavía.
- Firma de los binarios / verificación de que el propio launcher no ha sido manipulado. Razonable
  para una release pública de verdad, pero no hay nada que firmar todavía — se anota en
  `docs/STATUS.md`.
- Límite de ancho de banda o rate limit específico sobre `/files/`. Hoy no hay tráfico real; se
  revisita si se vuelve un problema.

## 5. Verificación

1. `dotnet build Epimeteo.sln`, `dotnet test`: en verde, sin warnings.
2. `dotnet run --project tools/Epimeteo.ReleaseTool -- release` genera `client-build/manifest.json`
   con el hash real de `client-build/README.md`.
3. Contra producción: `dotnet run --project tools/Epimeteo.Launcher -- --url https://epimeteo.waterressistan.duckdns.org --dir <tmp>`
   descarga `README.md`; segunda corrida sin cambios no vuelve a descargar nada; tras tocar el
   fichero en el servidor y regenerar el manifiesto, una tercera corrida sí lo reemplaza; un
   fichero puesto a mano en `<tmp>` que no está en el manifiesto se borra.
4. `curl https://epimeteo.waterressistan.duckdns.org/files/../appsettings.Production.json` (y
   variantes con `%2e%2e`) → 404, nunca el contenido del fichero.
5. `/status` y `/metrics` desde internet siguen en 404 (no se reabre el hallazgo de la Fase 13).

## 6. Resultado y hallazgos reales

### El hallazgo real: el despliegue tumbó producción durante unos segundos

`ReleasePaths.ResolveReleaseRoot()` sigue el mismo patrón que `Content/ContentPaths.cs` —
carpeta junto al ejecutable, o subir hasta `Epimeteo.sln`— y **lanza si no encuentra
`client-build/`**, a propósito: es mejor que el servidor no arranque a que `/files/` sirva desde
un sitio equivocado en silencio. El problema es que `deploy/publish.sh` sincronizaba `content/`
hacia `/opt/epimeteo` pero nunca aprendió a hacer lo mismo con `client-build/` — se escribió el
código nuevo sin actualizar el script de despliegue que lo tiene que alimentar.

El primer `bash deploy/publish.sh` de esta sesión reinició el servicio con el binario nuevo, que
inmediatamente lanzó esa excepción y **abortó** (`status=6/ABRT`); `Restart=always` de systemd lo
reintentó cada pocos segundos, siempre con el mismo fallo — el juego entero (WebSocket incluido,
no sólo `/files/`) estuvo caído en bucle de reinicio durante ese tramo. Se detectó al momento por
el propio `publish.sh` fallando en su comprobación de `/version` tras 15 s, se diagnosticó con
`journalctl -u epimeteo`, y se arregló añadiendo a `publish.sh` la misma sincronización con enlace
simbólico que ya tenía `content/` (`rsync` a `/opt/epimeteo/client-build/`, regenerar el
manifiesto con el binario ya compilado de `Epimeteo.ReleaseTool` —no `dotnet run`, que habría
reconstruido el proyecto sólo para esto—, enlazar en `$APP_DIR/client-build`). Un segundo
`publish.sh` con el script corregido levantó el servicio con normalidad.

**Lección concreta:** cuando un endpoint nuevo depende de un directorio que no existía antes,
comprobar que arranca en local no basta — hace falta comprobar también que el *script de
despliegue* sabe poner ese directorio donde el binario publicado lo espera. `dotnet run` en este
repositorio (el `AppContext.BaseDirectory` cae dentro del árbol con `Epimeteo.sln`) enmascara
justo este tipo de fallo, porque la segunda estrategia de `ResolveReleaseRoot()` siempre encuentra
algo. Sólo se ve corriendo el binario publicado tal cual queda en `/opt/epimeteo/app`.

### Verificación real ejecutada

1. ✅ `dotnet build` sin warnings (solución y cliente); `dotnet test`: **163/163 compartidos +
   317/317 servidor** (12 nuevos de `SafeFileResolverTests`).
2. ✅ `tools/Epimeteo.ReleaseTool` genera un manifiesto con el SHA-256 real, verificado a mano con
   `sha256sum`.
3. ✅ `tools/Epimeteo.Launcher` contra `https://epimeteo.waterressistan.duckdns.org` de verdad,
   tres corridas: descarga inicial, segunda corrida sin cambios (0 descargas), y con un fichero
   puesto a mano que no está en el manifiesto (se borra). Localmente, además, un cuarto caso que
   no se podía probar sólo contra producción sin tocar el servidor de verdad: cambiar el fichero
   servido, regenerar el manifiesto y comprobar que el launcher **reemplaza** el fichero antiguo
   por el nuevo (hash distinto → descarga; hash verificado tras descargar antes de mover el
   fichero final).
4. ✅ Traversal, desde internet, en varias formas: `..` literal → 404; `..%2f` y `%2e%2e`
   codificados → **400**, no 404 — ASP.NET Core/Kestrel normaliza y rechaza segmentos de punto
   codificados en la propia capa de routing, antes incluso de llegar a `SafeFileResolver`. No
   sustituye la comprobación explícita (una ruta con `..` sin codificar sí llega hasta el
   resolver, y ahí la para la primera capa), pero es una tercera capa gratuita que no estaba
   planeada y conviene dejar anotada.
5. ✅ `/status` y `/metrics` siguen en 404 desde internet: el hallazgo de la Fase 13 no se reabrió
   al tocar `Program.cs` ni el nginx instalado.

### Límite honesto

No hay ninguna build de Godot real todavía, así que todo lo anterior se verifica sobre un único
fichero de marcador. El mecanismo (manifiesto, servidor, launcher, limpieza de sobrantes) es el
mismo que hará falta el día que exista una build de verdad — no cambia con el tamaño ni el número
de ficheros— pero **no se ha probado con un volumen de datos real** (cientos de MB, cientos de
ficheros). Es lo esperable de un launcher que no puede haber probado contra un cliente que no
existe.
