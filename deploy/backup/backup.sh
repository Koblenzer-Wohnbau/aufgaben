#!/usr/bin/env bash
# =====================================================================
# Verschlüsselte Sicherung der Aufgaben-Datenbank mit restic
#
# Einmalig einrichten (als Admin-Benutzer, Werte in /etc/aufgaben-backup.env, chmod 600):
#   RESTIC_REPOSITORY=sftp:backup@backup.example.de:/srv/restic/aufgaben   (oder s3:..., rest:...)
#   RESTIC_PASSWORD=<langes Passwort: getrennt aufbewahren!>
#   AUFGABEN_DIR=/opt/aufgaben
#
#   sudo -E restic init         # einmalig, mit geladenen Variablen
#
# Täglich per systemd-Timer oder cron, z. B.:
#   15 2 * * *  root  /opt/aufgaben/deploy/backup/backup.sh >> /var/log/aufgaben-backup.log 2>&1
# =====================================================================
set -euo pipefail

ENV_FILE="${ENV_FILE:-/etc/aufgaben-backup.env}"
# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a

: "${RESTIC_REPOSITORY:?}" "${RESTIC_PASSWORD:?}" "${AUFGABEN_DIR:?}"

stamp=$(date +%Y%m%d-%H%M%S)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
umask 077

echo "[$(date -Is)] Sicherung startet"

# Konsistenter Abzug im laufenden Betrieb (pg_dump arbeitet mit einem Snapshot).
docker compose --project-directory "$AUFGABEN_DIR" exec -T db \
  pg_dump -U aufgaben -d aufgaben --format=custom --no-owner > "$work/aufgaben-$stamp.dump"

# Konfiguration ohne Klartext-Geheimnisse ist im Repository; .env und secrets/ separat mitsichern.
cp "$AUFGABEN_DIR/.env" "$work/env-$stamp"
cp -r "$AUFGABEN_DIR/secrets" "$work/secrets-$stamp" 2>/dev/null || true
# Eigene TLS-Zertifikate (internes Netz) und die Nginx-Konfiguration gehören zur Wiederherstellung.
# Das restic-Kennwort (/etc/aufgaben-backup.env) bewusst NICHT, es gehört getrennt verwahrt.
cp -r "$AUFGABEN_DIR/tls" "$work/tls-$stamp" 2>/dev/null || true
mkdir -p "$work/nginx-$stamp"
cp /etc/nginx/sites-available/aufgaben* "$work/nginx-$stamp/" 2>/dev/null || true

restic backup --host aufgaben --tag aufgaben "$work"
restic forget --host aufgaben --tag aufgaben --keep-daily 14 --keep-weekly 8 --keep-monthly 12 --prune
restic check --read-data-subset=5%

echo "[$(date -Is)] Sicherung fertig"
