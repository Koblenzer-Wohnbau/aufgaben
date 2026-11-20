#!/usr/bin/env bash
# =====================================================================
# Grundhärtung eines frischen Ubuntu-Servers (24.04/26.04) für Aufgaben
#
# Voraussetzung: Der Admin-Benutzer (nicht root) kann sich schon per SSH-Schlüssel anmelden
# (z. B. eingerichtet mit https://github.com/zerox80/ssh-hardening).
#
# Einfach starten – das Skript fragt alles Nötige ab und zeigt vor jeder Änderung eine Zusammenfassung:
#
#   sudo bash deploy/server/haerten.sh
#
# Ohne Rückfragen (Automatisierung) über Variablen, mit --yes ohne Bestätigung:
#
#   sudo ADMIN_USER=deinname SSH_ALLOW_FROM="192.168.1.0/24" HTTPS_ALLOW_FROM="" \
#        bash deploy/server/haerten.sh --yes
#
# SSH_ALLOW_FROM:   Netze/IPs für SSH, durch Leerzeichen getrennt. Leer = von überall (nur mit Schlüssel).
# HTTPS_ALLOW_FROM: Netze/IPs, die die App erreichen dürfen. Leer = öffentlich (nur hinter Entra-Login).
#
# Das Skript ist wiederholbar. Es ändert nichts an Docker-Containern.
# =====================================================================
set -euo pipefail

ASSUME_YES=0
for arg in "$@"; do
  case "$arg" in
    -y|--yes) ASSUME_YES=1 ;;
    -h|--help) sed -n '2,24p' "$0"; exit 0 ;;
    *) echo "Unbekannte Option: $arg (erlaubt: --yes, --help)" >&2; exit 2 ;;
  esac
done

if [[ $EUID -ne 0 ]]; then
  echo "Bitte mit sudo ausführen:  sudo bash $0" >&2
  exit 1
fi

# Rückfragen nur mit Terminal – ohne Terminal (Automatisierung) gelten die Variablen.
interactive=0
[[ -t 0 && -t 1 ]] && interactive=1
ask() { # Frage, Vorschlag
  local answer prompt="$1"
  [[ -n "${2:-}" ]] && prompt+=" [$2]"
  read -r -p "$prompt: " answer || true
  printf '%s' "${answer:-${2:-}}"
}
ask_yes() { # Frage, Vorschlag j/n
  local a; a="$(ask "$1 (j/n)" "$2" | tr '[:upper:]' '[:lower:]')"
  [[ "$a" =~ ^(j|ja|y|yes)$ ]]
}
# Liegt eine IPv4-Adresse in einem Netz (a.b.c.d/nn)? 2 = nicht prüfbar (z. B. IPv6)
ip_in_net() {
  local ip="$1" cidr="$2" a b c d ipn netn bits mask
  [[ "$cidr" == */* ]] || cidr="$cidr/32"
  bits="${cidr#*/}"
  [[ "$ip" =~ ^[0-9]+(\.[0-9]+){3}$ && "${cidr%/*}" =~ ^[0-9]+(\.[0-9]+){3}$ && "$bits" =~ ^[0-9]+$ ]] || return 2
  IFS=. read -r a b c d <<<"$ip"; ipn=$(( (a << 24) | (b << 16) | (c << 8) | d ))
  IFS=. read -r a b c d <<<"${cidr%/*}"; netn=$(( (a << 24) | (b << 16) | (c << 8) | d ))
  mask=$(( bits == 0 ? 0 : (0xFFFFFFFF << (32 - bits)) & 0xFFFFFFFF ))
  (( (ipn & mask) == (netn & mask) ))
}
user_ok() { # gibt den Grund aus, wenn der Benutzer nicht passt
  local home
  if ! getent passwd "$1" >/dev/null; then echo "Benutzer '$1' gibt es auf diesem Server nicht."; return 1; fi
  [[ "$1" != root ]] || { echo "Bitte einen eigenen Benutzer statt root nehmen – root wird für SSH gesperrt."; return 1; }
  home="$(getent passwd "$1" | cut -d: -f6 || true)"
  if [[ -z "$home" || ! -s "$home/.ssh/authorized_keys" ]]; then
    echo "'$1' hat keinen SSH-Schlüssel in ~/.ssh/authorized_keys – danach wäre kein Login mehr möglich."
    return 1
  fi
}

# Adresse, von der du gerade per SSH verbunden bist (auch unter sudo)
client_ip="$(who -m 2>/dev/null | sed -n 's/.*(\([^)]*\)).*/\1/p' | head -n 1 || true)"
if [[ -z "$client_ip" ]]; then
  # unter sudo fehlt SSH_CLIENT meist – dann bleibt es leer (kein Abbruch durch "set -u")
  client_ip="${SSH_CLIENT:-}"
  client_ip="${client_ip%% *}"
fi
suggested_net=""
[[ "$client_ip" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)\.[0-9]+$ ]] && suggested_net="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.${BASH_REMATCH[3]}.0/24"

SSH_PORT="${SSH_PORT:-22}"
if (( interactive )); then
  echo "Härtung für Aufgaben – ein paar Fragen, geändert wird erst nach deiner Bestätigung."
  echo ""
  # 1. Admin-Benutzer: Vorschlag ist der Benutzer, der sudo aufgerufen hat
  ADMIN_USER="${ADMIN_USER:-${SUDO_USER:-}}"
  while true; do
    ADMIN_USER="$(ask "Admin-Benutzer, der sich danach per SSH-Schlüssel anmeldet" "$ADMIN_USER")"
    if reason="$(user_ok "$ADMIN_USER")"; then break; fi
    echo "  -> $reason"
    ADMIN_USER=""
  done
  # 2. SSH nur aus bestimmten Netzen?
  echo ""
  [[ -n "$client_ip" ]] && echo "Du bist gerade verbunden von: $client_ip"
  while true; do
    SSH_ALLOW_FROM="$(ask "SSH erlauben von (Netze/IPs mit Leerzeichen, '-' = von überall)" "${SSH_ALLOW_FROM:-$suggested_net}")"
    [[ "$SSH_ALLOW_FROM" == "-" ]] && SSH_ALLOW_FROM=""
    [[ -z "$SSH_ALLOW_FROM" || -z "$client_ip" ]] && break
    inside=0
    for net in $SSH_ALLOW_FROM; do
      if ip_in_net "$client_ip" "$net"; then inside=1; elif [[ $? -eq 2 ]]; then inside=1; fi
    done
    (( inside )) && break
    echo "  -> Achtung: $client_ip liegt NICHT in '$SSH_ALLOW_FROM' – du würdest dich aussperren."
    ask_yes "  Trotzdem so übernehmen?" "n" && break
  done
  # 3. HTTPS öffentlich oder eingeschränkt?
  echo ""
  HTTPS_ALLOW_FROM="$(ask "HTTPS (App) erlauben von (Netze/IPs, '-' = öffentlich, z. B. hinter FortiGate)" "${HTTPS_ALLOW_FROM:--}")"
  [[ "$HTTPS_ALLOW_FROM" == "-" ]] && HTTPS_ALLOW_FROM=""
else
  ADMIN_USER="${ADMIN_USER:-}"
  SSH_ALLOW_FROM="${SSH_ALLOW_FROM:-}"
  HTTPS_ALLOW_FROM="${HTTPS_ALLOW_FROM:-}"
  if [[ -z "$ADMIN_USER" ]]; then
    echo "Abbruch: ohne Terminal bitte ADMIN_USER setzen (siehe Kopf dieses Skripts)." >&2
    exit 1
  fi
  if ! reason="$(user_ok "$ADMIN_USER")"; then echo "Abbruch: $reason" >&2; exit 1; fi
fi

echo ""
echo "==================== Zusammenfassung ===================="
echo "  Admin-Benutzer:    $ADMIN_USER (einziger SSH-Benutzer, nur mit Schlüssel)"
echo "  SSH (Port $SSH_PORT):     ${SSH_ALLOW_FROM:-von überall (nur mit Schlüssel, Rate-Limit)}"
echo "  HTTPS (Port 443):  ${HTTPS_ALLOW_FROM:-öffentlich}"
echo "  Außerdem:          Updates, automatische Sicherheitsupdates (Neustart 03:30), Firewall neu"
echo "                     (alle anderen Regeln werden gelöscht), fail2ban, Kernel-Härtung, Docker, Nginx"
echo "========================================================="
if (( interactive && ! ASSUME_YES )); then
  ask_yes "So ausführen?" "j" || { echo "Abgebrochen – nichts geändert."; exit 0; }
fi
echo ""

echo "==> Pakete aktualisieren und Grundwerkzeuge installieren"
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get -y -q upgrade
apt-get -y -q install ufw unattended-upgrades apt-listchanges fail2ban nginx certbot ca-certificates curl gnupg restic auditd

echo "==> Automatische Sicherheitsupdates"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT::Periodic::AutocleanInterval "7";
EOF
cat > /etc/apt/apt.conf.d/52aufgaben-unattended <<'EOF'
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "03:30";
Unattended-Upgrade::Remove-Unused-Dependencies "true";
EOF

echo "==> SSH: nur Schlüssel, kein root, kein Passwort"
install -d -m 755 /etc/ssh/sshd_config.d
cat > /etc/ssh/sshd_config.d/10-aufgaben-haertung.conf <<EOF
Port ${SSH_PORT}
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
AuthenticationMethods publickey
AllowUsers ${ADMIN_USER}
X11Forwarding no
AllowAgentForwarding no
AllowTcpForwarding no
MaxAuthTries 3
LoginGraceTime 20
ClientAliveInterval 300
ClientAliveCountMax 2
EOF
sshd -t
# Ubuntu 24.04 startet SSH über Socket-Aktivierung: Den Port legt dann ssh.socket fest,
# nicht die sshd-Konfiguration. Ohne Neustart des Sockets lauscht SSH weiter auf dem alten
# Port – und die Firewall unten sperrt ihn. Deshalb den Socket neu laden.
if systemctl is-active --quiet ssh.socket; then
  systemctl daemon-reload
  systemctl restart ssh.socket
fi
systemctl reload ssh || systemctl reload sshd

echo "==> Firewall: alles zu außer SSH, 80 (nur Zertifikat/Weiterleitung) und 443"
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
# Kommentare ohne Apostroph und Klammern – ufw meldet sonst "Invalid syntax".
if [[ -n "$SSH_ALLOW_FROM" ]]; then
  for src in $SSH_ALLOW_FROM; do
    ufw allow from "$src" to any port "$SSH_PORT" proto tcp comment "SSH Firma"
  done
else
  ufw limit "$SSH_PORT"/tcp comment "SSH mit Rate-Limit"
fi
# Port 80 bleibt offen: Let's Encrypt prüft von wechselnden Adressen aus.
# Nginx liefert dort nur die Prüfdatei und eine Weiterleitung – keine App.
ufw allow 80/tcp comment "HTTP Zertifikat und Weiterleitung"
if [[ -n "$HTTPS_ALLOW_FROM" ]]; then
  for src in $HTTPS_ALLOW_FROM; do
    ufw allow from "$src" to any port 443 proto tcp comment "HTTPS Firma"
  done
  echo "    HTTPS nur für: $HTTPS_ALLOW_FROM"
else
  ufw allow 443/tcp comment "HTTPS Aufgaben"
fi
ufw --force enable

echo "==> fail2ban für SSH und Nginx"
cat > /etc/fail2ban/jail.d/aufgaben.local <<EOF
[DEFAULT]
bantime = 1h
findtime = 10m
maxretry = 5
backend = systemd

[sshd]
enabled = true
port = ${SSH_PORT}

[nginx-limit-req]
enabled = true
port = http,https
logpath = /var/log/nginx/error.log
backend = auto
EOF
systemctl enable --now fail2ban
systemctl restart fail2ban

echo "==> Kernel-Netzwerkhärtung"
cat > /etc/sysctl.d/90-aufgaben.conf <<'EOF'
net.ipv4.conf.all.rp_filter = 1
net.ipv4.conf.default.rp_filter = 1
net.ipv4.conf.all.accept_redirects = 0
net.ipv6.conf.all.accept_redirects = 0
net.ipv4.conf.all.send_redirects = 0
net.ipv4.conf.all.accept_source_route = 0
net.ipv4.tcp_syncookies = 1
net.ipv4.icmp_echo_ignore_broadcasts = 1
kernel.kptr_restrict = 2
kernel.dmesg_restrict = 1
fs.protected_hardlinks = 1
fs.protected_symlinks = 1
EOF
sysctl --system >/dev/null

echo "==> Docker (offizielles Repository) – falls noch nicht vorhanden"
if ! command -v docker >/dev/null; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  # shellcheck disable=SC1091
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get -y -q install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

echo "==> Docker-Daemon härten"
install -d /etc/docker
cat > /etc/docker/daemon.json <<'EOF'
{
  "live-restore": true,
  "no-new-privileges": true,
  "userland-proxy": false,
  "icc": false,
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "5" }
}
EOF
systemctl restart docker

echo "==> Nginx-Grundeinstellungen"
sed -i 's/# server_tokens off;/server_tokens off;/' /etc/nginx/nginx.conf || true
install -d -m 755 /var/www/certbot
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

echo
echo "Fertig. Nächste Schritte:"
echo "  * In einer NEUEN Sitzung prüfen, dass der SSH-Login noch klappt (alte Sitzung offen lassen!)"
echo "  * Docker-Ports nie auf 0.0.0.0 veröffentlichen – Docker umgeht ufw. compose.yaml nutzt 127.0.0.1."
echo "  * Zertifikat holen, z. B. per DNS-Challenge (Cloudflare) oder per Webroot:"
echo "      certbot certonly --webroot -w /var/www/certbot -d <eure-domain> --deploy-hook 'systemctl reload nginx'"
echo "  * deploy/nginx/aufgaben-oeffentlich.conf einspielen, Backups einrichten (deploy/backup)"
