#!/usr/bin/env bash
# Vuelca la base de datos del juego y purga los backups de hace más de 14 días.
#
# Lee las credenciales de PGHOST/PGPORT/PGDATABASE/PGUSER/PGPASSWORD, normalmente puestas por el
# EnvironmentFile de epimeteo-backup.service (/opt/epimeteo/backup.env, ver §4 paso 6 de la fase).
# No lleva la contraseña en el propio script para que este fichero se pueda versionar en git.
set -euo pipefail

BACKUP_DIR="${EPIMETEO_BACKUP_DIR:-/opt/epimeteo/backups}"
RETENTION_DAYS="${EPIMETEO_BACKUP_RETENTION_DAYS:-14}"
STAMP="$(date -u +%Y-%m-%dT%H%M%SZ)"
OUT_FILE="$BACKUP_DIR/epimeteo-$STAMP.sql.gz"

mkdir -p "$BACKUP_DIR"

echo "==> Volcando ${PGDATABASE:-epimeteo} a $OUT_FILE"
pg_dump --no-owner --no-privileges | gzip > "$OUT_FILE"

if [ ! -s "$OUT_FILE" ]; then
    echo "El volcado salió vacío; se descarta." >&2
    rm -f "$OUT_FILE"
    exit 1
fi

echo "==> Purgando backups de hace más de $RETENTION_DAYS días"
find "$BACKUP_DIR" -maxdepth 1 -name 'epimeteo-*.sql.gz' -mtime "+$RETENTION_DAYS" -print -delete

echo "==> Hecho: $(du -h "$OUT_FILE" | cut -f1)"
