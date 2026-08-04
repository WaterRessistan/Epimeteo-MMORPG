#!/usr/bin/env bash
# Publica el servidor y lo despliega en /opt/epimeteo. Uso: deploy/publish.sh
#
# No publica si el build o los tests fallan: nunca se sube código sin verificar a producción
# (docs/fases/FASE-05-despliegue.md §5). Necesita sudo para escribir en /opt/epimeteo (propiedad
# del usuario de sistema `epimeteo`) y para reiniciar el servicio.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY_ROOT="/opt/epimeteo"
APP_DIR="$DEPLOY_ROOT/app"
CONTENT_DIR="$DEPLOY_ROOT/content"
PUBLISH_DIR="$(mktemp -d)"
trap 'rm -rf "$PUBLISH_DIR"' EXIT

cd "$REPO_ROOT"

echo "==> Comprobando que el árbol de despliegue existe (usuario epimeteo, /opt/epimeteo)"
if ! id epimeteo >/dev/null 2>&1; then
    echo "El usuario de sistema 'epimeteo' no existe todavía." >&2
    echo "Ver docs/fases/FASE-05-despliegue.md §4, paso 4, antes de publicar." >&2
    exit 1
fi
# sudo test, no [ -f ... ]: /opt/epimeteo/app es propiedad de epimeteo, no de quien lanza este
# script, así que un simple [ -f ] diría "falta" aunque el fichero esté ahí, sólo porque el
# usuario que publica no puede atravesar el directorio para comprobarlo.
if ! sudo test -f "$APP_DIR/appsettings.Production.json"; then
    # Content root de ASP.NET Core = WorkingDirectory del servicio = $APP_DIR (junto al .dll), no
    # $DEPLOY_ROOT: si estuviera un nivel más arriba, el servidor arrancaría sin cadena de
    # conexión y fallaría con el mismo error que en desarrollo sin appsettings.Development.json.
    echo "Falta $APP_DIR/appsettings.Production.json (ver appsettings.Production.json.example)." >&2
    echo "Se crea una vez a mano; publish.sh no lo toca (ver §4 paso 4 y §5 de la fase)." >&2
    exit 1
fi

echo "==> dotnet build (warnings como error en shared/ y server/, CLAUDE.md §4)"
dotnet build Epimeteo.sln -c Release

echo "==> dotnet test"
dotnet test Epimeteo.sln -c Release --no-build

echo "==> dotnet publish (framework-dependent: ya hay dotnet 8 instalado en el servidor)"
dotnet publish server/Epimeteo.Server -c Release -o "$PUBLISH_DIR" --no-self-contained

# El SDK copia appsettings*.json al publicar, incluido appsettings.Development.json si existe en
# el árbol de trabajo (tiene la contraseña de Postgres de desarrollo). No tiene nada que hacer en
# /opt/epimeteo: se borra del directorio de publicación antes de sincronizar, no se manda ni de
# paso.
rm -f "$PUBLISH_DIR/appsettings.Development.json"

echo "==> Sincronizando /opt/epimeteo/app"
sudo mkdir -p "$APP_DIR/logs"
# appsettings.Production.json no viene en $PUBLISH_DIR (está en /opt/epimeteo/app pero no en el
# repositorio: son secretos, CLAUDE.md §4). --exclude evita que --delete lo borre por no estar
# en el origen — sin esto cada publish dejaría el servidor sin cadena de conexión.
sudo rsync -a --delete --exclude 'logs/' --exclude 'appsettings.Production.json' "$PUBLISH_DIR"/ "$APP_DIR"/

echo "==> Sincronizando content/ (fuente de verdad versionada en git, CLAUDE.md §3)"
sudo mkdir -p "$CONTENT_DIR"
sudo rsync -a --delete "$REPO_ROOT/content"/ "$CONTENT_DIR"/
# El servidor busca content/ junto al binario (Content/ContentPaths.cs): un enlace, no una copia
# más, para no duplicar los ficheros ni tener que recordar sincronizar dos veces.
sudo ln -sfn "$CONTENT_DIR" "$APP_DIR/content"

echo "==> Permisos (usuario dedicado, CLAUDE.md §2)"
sudo chown -R epimeteo:epimeteo "$DEPLOY_ROOT"
# mktemp crea $PUBLISH_DIR en 700 y rsync -a arrastra ese modo al directorio de destino junto con
# el contenido: sin este chmod, /opt/epimeteo/app se queda en 700 por accidente de mktemp, no por
# decisión. 755 explícito — sólo appsettings.Production.json lleva secretos, y ese va aparte.
sudo chmod 755 "$APP_DIR"
sudo chmod 600 "$APP_DIR/appsettings.Production.json"

echo "==> Reiniciando el servicio"
sudo systemctl restart epimeteo

echo "==> Comprobando que levanta"
for _ in $(seq 1 15); do
    if curl -fsS http://127.0.0.1:5101/version >/tmp/epimeteo-publish-version.json 2>/dev/null; then
        echo "Arriba: $(cat /tmp/epimeteo-publish-version.json)"
        rm -f /tmp/epimeteo-publish-version.json
        exit 0
    fi
    sleep 1
done

echo "El servicio no respondió a /version en 15 s. Revisa: sudo journalctl -u epimeteo -n 50" >&2
exit 1
