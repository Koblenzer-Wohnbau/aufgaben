#!/usr/bin/env bash
# Prüft deploy/server/nginx-einrichten.sh in einem frischen Ubuntu-Container mit echtem Nginx,
# Test-Zertifikat und einer Attrappe der App auf 127.0.0.1:8090.
#   docker run --rm -v "$PWD:/src:ro" ubuntu:24.04 bash /src/tests/deploy/nginx-einrichten.sh
# Gesucht wird wörtlicher Text mit $ (keine Ersetzung gewollt).
# shellcheck disable=SC2016
set -euo pipefail
fail() { echo "FEHLER: $*" >&2; exit 1; }
step() { printf '\n---- %s\n' "$*"; }

step "Pakete"
apt-get update -qq >/dev/null
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq nginx openssl curl iproute2 python3 >/dev/null 2>&1

cp -r /src /opt/aufgaben
cd /opt/aufgaben
script=deploy/server/nginx-einrichten.sh
conf=/etc/nginx/sites-available/aufgaben.conf
domain=aufgaben.example.org
printf 'Aufgaben__BaseUrl=https://%s\nAPP_PORT=8090\n' "$domain" >.env

# App-Attrappe: antwortet mit dem Host-Kopf, den Nginx weiterreicht
cat >/tmp/app.py <<'PY'
from http.server import BaseHTTPRequestHandler, HTTPServer
class H(BaseHTTPRequestHandler):
    def do_GET(self):
        body = ("host=%s proto=%s" % (self.headers.get("Host"), self.headers.get("X-Forwarded-Proto"))).encode()
        self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *a): pass
HTTPServer(("127.0.0.1", 8090), H).serve_forever()
PY
python3 /tmp/app.py &
nginx
sleep 1
request() { curl -sk --max-time 5 --resolve "$domain:443:127.0.0.1" "https://$domain/" || true; }

step "Ohne Zertifikat: klarer Abbruch, nichts angelegt"
if bash "$script" --yes </dev/null >/tmp/out 2>&1; then cat /tmp/out; fail "hätte abbrechen müssen"; fi
grep -q "Kein Zertifikat" /tmp/out || { cat /tmp/out; fail "Meldung fehlt"; }
[[ ! -e "$conf" ]] || fail "Konfiguration trotzdem angelegt"
echo "ok"

le="/etc/letsencrypt/live/$domain"
mkdir -p "$le"
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=$domain" -keyout "$le/privkey.pem" -out "$le/fullchain.pem" 2>/dev/null

step "Erster Lauf (öffentlich, Domain aus .env)"
bash "$script" --yes --networks "192.168.1.0/24" </dev/null | tee /tmp/lauf1
[[ -L /etc/nginx/sites-enabled/aufgaben.conf ]] || fail "nicht in sites-enabled eingeschaltet"
grep -q "server_name $domain;" "$conf" || fail "Domain nicht eingesetzt"
[[ "$(grep -c '^    proxy_set_header' "$conf")" == 4 ]] || fail "proxy_set_header-Zeilen fehlen"
[[ "$(grep -c '192.168.1.0/24 1;' "$conf")" == 1 ]] || fail "Netz nicht genau einmal im geo-Block"
grep -q '^    # if (\$aufgaben_firma = 0)' "$conf" || fail "444-Block nicht ausgeschaltet"
[[ "$(request)" == "host=$domain proto=https" ]] || fail "App bekommt falschen Host/Proto: $(request)"
echo "ok: eingeschaltet, Kopfzeilen vollständig, App sieht host=$domain proto=https"

step "Zweiter Lauf mit denselben Werten ergibt dieselbe Datei"
cp "$conf" /tmp/vorher
bash "$script" --yes --networks "192.168.1.0/24" </dev/null >/dev/null
cmp -s /tmp/vorher "$conf" || { diff /tmp/vorher "$conf"; fail "nicht wiederholbar"; }
compgen -G "$conf.vorher-*" >/dev/null || fail "keine Sicherung der vorherigen Fassung"
echo "ok: identisch, vorherige Fassung gesichert"

step "Nur Firmennetz"
bash "$script" --yes --company-only --networks "10.0.0.0/8" </dev/null >/dev/null
grep -q '^    if (\$aufgaben_firma = 0)' "$conf" || fail "444-Block nicht aktiv"
grep -q '127.0.0.0/8 1;' "$conf" || fail "Loopback fehlt im geo-Block"
[[ "$(request)" == "host=$domain proto=https" ]] || fail "vom Server selbst nicht erreichbar"
echo "ok: 444 aktiv, der Server selbst bleibt erreichbar"

step "Ungültige Eingaben"
if bash "$script" --yes --company-only --networks "" </dev/null >/tmp/out 2>&1; then fail "Firmennetz ohne Netze angenommen"; fi
grep -q "aussperren" /tmp/out || { cat /tmp/out; fail "Meldung fehlt"; }
if bash "$script" --yes --networks "kein-netz" </dev/null >/tmp/out 2>&1; then fail "ungültiges Netz angenommen"; fi
echo "ok"

step "Kaputte Vorlage: nginx -t schlägt fehl, vorherige Fassung bleibt"
cp "$conf" /tmp/gut
echo "kaputt;" >>deploy/nginx/aufgaben-oeffentlich.conf
if bash "$script" --yes --networks "10.0.0.0/8" --company-only </dev/null >/tmp/out 2>&1; then fail "kaputte Konfiguration übernommen"; fi
cmp -s /tmp/gut "$conf" || fail "vorherige Fassung nicht wiederhergestellt"
nginx -t 2>/dev/null || fail "Nginx-Konfiguration danach ungültig"
echo "ok: zurückgerollt"

echo ""
echo "==== nginx-einrichten.sh: alle Prüfungen bestanden"
