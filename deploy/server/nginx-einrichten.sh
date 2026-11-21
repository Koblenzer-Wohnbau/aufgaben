#!/usr/bin/env bash
# =====================================================================
# Nginx für Aufgaben einrichten: vollständig, geprüft, wiederholbar.
#
# Nimmt die Vorlage deploy/nginx/aufgaben-oeffentlich.conf, setzt Domain, Port und die internen Netze
# ein, legt die Seite unter /etc/nginx/sites-available/aufgaben.conf ab, schaltet sie ein, prüft mit
# "nginx -t" und lädt Nginx neu. Schlägt die Prüfung fehl, kommt die vorherige Fassung zurück.
#
#   sudo bash deploy/server/nginx-einrichten.sh           - fragt nach, Vorschläge aus .env und Netz
#   sudo bash deploy/server/nginx-einrichten.sh --yes     - Vorschläge ohne Rückfrage übernehmen
#
# Optionen (überschreiben die Vorschläge):
#   --domain NAME        Standard: Name aus Aufgaben__BaseUrl in .env
#   --networks "A B"     interne Netze (CIDR), für die keine Limits pro Adresse gelten.
#                        Standard: die Netze der Netzwerkkarten dieses Servers
#   --public             App von überall erreichbar (Standard; die Anmeldung schützt Entra ID)
#   --company-only       nur aus den internen Netzen erreichbar, alle anderen Verbindungen werden
#                        ohne Antwort geschlossen (444)
#   --yes                ohne Rückfrage
#
# Voraussetzung: TLS-Zertifikat unter /etc/letsencrypt/live/<domain>/: z. B. mit
#   sudo bash -c "$(curl -fsSL https://raw.githubusercontent.com/zerox80/certbot-sexy/main/zertifikat-cloudflare.sh)"
# =====================================================================
# Meldungen nutzen deutsche Anführungszeichen („…“) mit Absicht.
# shellcheck disable=SC1111
set -euo pipefail

say()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
ok()   { printf '    \033[32mok\033[0m  %s\n' "$*"; }
warn() { printf '    \033[33m!!\033[0m  %s\n' "$*" >&2; }
fail() { printf '\n\033[31mFEHLER:\033[0m %s\n' "$*" >&2; exit 1; }

domain="" networks="" networks_given=0 mode="" assume_yes=0
while (( $# )); do
  case "$1" in
    --domain) domain="${2:-}"; shift 2 ;;
    --networks) networks="${2:-}"; networks_given=1; shift 2 ;;
    --public) mode="public"; shift ;;
    --company-only) mode="company"; shift ;;
    -y|--yes) assume_yes=1; shift ;;
    -h|--help) sed -n '2,23p' "$0"; exit 0 ;;
    *) fail "Unbekannte Option: $1 (siehe --help)" ;;
  esac
done

(( EUID == 0 )) || fail "Bitte mit sudo ausführen: sudo bash $0"
root="$(cd "$(dirname "$0")/../.." && pwd)"
template="$root/deploy/nginx/aufgaben-oeffentlich.conf"
env_file="$root/.env"
target="${NGINX_SITES_DIR:-/etc/nginx/sites-available}/aufgaben.conf"
enabled_dir="${NGINX_ENABLED_DIR:-/etc/nginx/sites-enabled}"
le_dir="${LE_LIVE_DIR:-/etc/letsencrypt/live}"
[[ -f "$template" ]] || fail "Vorlage $template fehlt."
command -v nginx >/dev/null || fail "Nginx ist nicht installiert (sudo apt install -y nginx oder deploy/server/haerten.sh)."

interactive=0
[[ -t 0 && -t 1 ]] && interactive=1
ask() { # Frage, Vorschlag
  local answer
  if (( ! interactive || assume_yes )); then printf '%s' "$2"; return; fi
  read -r -p "    $1 [$2]: " answer || true
  printf '%s' "${answer:-$2}"
}
env_value() { [[ -f "$env_file" ]] && sed -n "s/^$1=//p" "$env_file" | tail -n 1 | tr -d '\r' || true; }

# ---------------------------------------------------------------- Werte bestimmen
say "Nginx für Aufgaben einrichten"
if [[ -z "$domain" ]]; then
  base="$(env_value Aufgaben__BaseUrl)"
  suggestion="${base#*://}"; suggestion="${suggestion%%/*}"; suggestion="${suggestion%%:*}"
  [[ -n "$suggestion" && "$suggestion" != aufgaben.example.de ]] || suggestion=""
  domain="$(ask "Domain der App" "$suggestion")"
fi
[[ "$domain" =~ ^[a-z0-9]([a-z0-9.-]*[a-z0-9])?\.[a-z]{2,}$ ]] || fail "Keine gültige Domain: „$domain“ (in .env Aufgaben__BaseUrl setzen oder --domain angeben)."

port="$(env_value APP_PORT)"; port="${port:-8090}"
[[ "$port" =~ ^[0-9]+$ ]] || fail "APP_PORT in .env ist keine Zahl."

cert="$le_dir/$domain/fullchain.pem"
[[ -f "$cert" && -f "$le_dir/$domain/privkey.pem" ]] || fail "Kein Zertifikat unter $le_dir/$domain/, zuerst das Zertifikat holen:
       sudo bash -c \"\$(curl -fsSL https://raw.githubusercontent.com/zerox80/certbot-sexy/main/zertifikat-cloudflare.sh)\""
ok "Zertifikat für $domain gefunden"

if (( ! networks_given )); then
  # Vorschlag: die Netze der eigenen Netzwerkkarten (z. B. 192.168.1.183/24 → 192.168.1.0/24)
  suggestion=""
  while read -r cidr; do
    ip="${cidr%/*}" bits="${cidr#*/}"
    IFS=. read -r a b c d <<<"$ip"
    n=$(( (a << 24 | b << 16 | c << 8 | d) & (bits == 0 ? 0 : (0xFFFFFFFF << (32 - bits)) & 0xFFFFFFFF) ))
    suggestion+="$(( n >> 24 & 255 )).$(( n >> 16 & 255 )).$(( n >> 8 & 255 )).$(( n & 255 ))/$bits "
  done < <(ip -o -4 addr show scope global 2>/dev/null | awk '{print $4}')
  networks="$(ask "Interne Netze (CIDR, mit Leerzeichen; '-' = keine)" "${suggestion% }")"
  [[ "$networks" == "-" ]] && networks=""
fi
for net in $networks; do
  [[ "$net" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}(/[0-9]{1,2})?$ ]] || fail "Kein gültiges Netz: „$net“ (Beispiel: 192.168.1.0/24)."
done

if [[ -z "$mode" ]]; then
  answer="$(ask "Von außen erreichbar (öffentlich, Anmeldung schützt Entra), oder nur Firmennetz? (o/f)" "o")"
  case "$answer" in f|F|firma|nur*) mode="company" ;; *) mode="public" ;; esac
fi
if [[ "$mode" == company && -z "$networks" ]]; then
  fail "„Nur Firmennetz“ ohne interne Netze würde alle aussperren. Netze angeben oder --public."
fi

echo ""
echo "    Domain:         $domain"
echo "    App-Port:       127.0.0.1:$port"
echo "    Interne Netze:  ${networks:-keine}"
if [[ "$mode" == company ]]; then echo "    Erreichbar:     nur aus den internen Netzen"
else echo "    Erreichbar:     von überall (Anmeldung über Entra ID)"; fi
if (( interactive && ! assume_yes )); then
  read -r -p "    So einrichten? (j/n) [j]: " answer || true
  [[ "${answer:-j}" =~ ^(j|ja|y|yes)$ ]] || { echo "    Abgebrochen, nichts geändert."; exit 0; }
fi

# ---------------------------------------------------------------- Konfiguration schreiben
say "Konfiguration schreiben und prüfen"
geo_lines="    127.0.0.0/8 1;"
for net in $networks; do geo_lines+=$'\n'"    $net 1;"; done
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
awk -v domain="$domain" -v port="$port" -v geo="$geo_lines" -v mode="$mode" '
  { gsub(/aufgaben\.example\.de/, domain); gsub(/127\.0\.0\.1:8090/, "127.0.0.1:" port) }
  /^geo \$aufgaben_firma/ { in_geo = 1 }
  in_geo && /^[[:space:]]*default 0;/ { print; print geo; next }
  in_geo && /^}/ { in_geo = 0 }
  # Beispiel-Netze der Vorlage weglassen
  in_geo && /^[[:space:]]*#/ { next }
  /if \(\$aufgaben_firma = 0\)/ { in_block = 1 }
  in_block {
    if (mode == "public") { sub(/^    /, "    # ") }
    print
    if ($0 ~ /^[[:space:]]*#?[[:space:]]*}[[:space:]]*$/) { in_block = 0 }
    next
  }
  { print }
' "$template" >"$tmp"

backup=""
if [[ -f "$target" ]]; then
  backup="$target.vorher-$(date +%Y%m%d-%H%M%S)"
  cp -p "$target" "$backup"
fi
install -m 644 "$tmp" "$target"
ln -sfn "$target" "$enabled_dir/aufgaben.conf"
if [[ -L "$enabled_dir/default" ]]; then rm -f "$enabled_dir/default"; ok "Standardseite von Nginx abgeschaltet"; fi

if ! nginx -t 2>"$tmp"; then
  cat "$tmp" >&2
  if [[ -n "$backup" ]]; then mv -f "$backup" "$target"; warn "Vorherige Fassung wiederhergestellt."
  else rm -f "$target" "$enabled_dir/aufgaben.conf"; fi
  fail "nginx -t meldet einen Fehler, nichts übernommen."
fi
ok "nginx -t: Konfiguration gültig"
if [[ -n "$backup" ]]; then ok "vorherige Fassung gesichert: $backup"; fi
if command -v systemctl >/dev/null && systemctl is-active --quiet nginx; then
  systemctl reload nginx
else
  nginx -s reload 2>/dev/null || nginx
fi
ok "Nginx neu geladen"

# ---------------------------------------------------------------- Prüfen
say "Prüfen"
code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 --resolve "$domain:443:127.0.0.1" "https://$domain/" || true)"
case "$code" in
  200|302|401) ok "https://$domain/ antwortet auf dem Server ($code)" ;;
  502|504) warn "Nginx läuft, aber die App antwortet nicht ($code): läuft sie? sudo docker compose ps" ;;
  *) warn "Unerwartete Antwort ($code): sudo tail /var/log/nginx/error.log" ;;
esac
echo ""
echo "    Im Firmennetz muss $domain auf die interne Adresse dieses Servers zeigen (Split-DNS auf dem"
echo "    Domaincontroller). Sonst laufen interne Aufrufe über die öffentliche Adresse der Firewall,"
echo "    und die leitet sie meist nicht zurück ins eigene Netz."
