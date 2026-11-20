#!/usr/bin/env bash
# =====================================================================
# Wiederherstellungstest: spielt die letzte Sicherung in eine WEGWERF-Datenbank
# ein und zählt die Datensätze. Berührt die laufende Datenbank nicht.
#
#   sudo ENV_FILE=/etc/aufgaben-backup.env ./restore-test.sh
# =====================================================================
set -euo pipefail

ENV_FILE="${ENV_FILE:-/etc/aufgaben-backup.env}"
# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a

work=$(mktemp -d)
container="aufgaben-restore-test-$$"
trap 'docker rm -f "$container" >/dev/null 2>&1 || true; rm -rf "$work"' EXIT

restic restore latest --host aufgaben --tag aufgaben --target "$work"
dump=$(find "$work" -name 'aufgaben-*.dump' | sort | tail -n1)
[[ -n "$dump" ]] || { echo "Kein Dump in der Sicherung gefunden" >&2; exit 1; }

docker run -d --name "$container" --network none -e POSTGRES_PASSWORD=test -e POSTGRES_USER=aufgaben -e POSTGRES_DB=aufgaben postgres:17-alpine >/dev/null
for _ in $(seq 1 30); do
  docker exec "$container" pg_isready -U aufgaben >/dev/null 2>&1 && break
  sleep 1
done
docker cp "$dump" "$container":/tmp/restore.dump
docker exec "$container" pg_restore -U aufgaben -d aufgaben --no-owner /tmp/restore.dump

echo "Wiederhergestellte Sicherung: $(basename "$dump")"
for table in users projects tasks task_events templates; do
  count=$(docker exec "$container" psql -U aufgaben -d aufgaben -tAc "select count(*) from $table")
  printf '  %-12s %s\n' "$table" "$count"
done
echo "Wiederherstellungstest erfolgreich – Datum und Zahlen im Betriebsprotokoll festhalten."
