#!/usr/bin/env bash
# =====================================================================
# Grundhärtung eines frischen Ubuntu-Servers (24.04/26.04) für Aufgaben
#
# Aufruf als root bzw. mit sudo, NACHDEM:
#   1. ein eigener Admin-Benutzer existiert (nicht root),
#   2. dessen SSH-Schlüssel in ~/.ssh/authorized_keys liegt,
#   3. ein Login mit diesem Schlüssel in einer ZWEITEN Sitzung getestet wurde.
#
#   sudo ADMIN_USER=deinname \
#        SSH_ALLOW_FROM="203.0.113.10/32" \
#        HTTPS_ALLOW_FROM="203.0.113.10/32 198.51.100.0/29" ./haerten.sh
#
# SSH_ALLOW_FROM:   öffentliche IP(s) der Firma für SSH, durch Leerzeichen getrennt.
#                   Leer = SSH von überall (nur mit Schlüssel) – schwächer.
# HTTPS_ALLOW_FROM: öffentliche IP(s), die die App überhaupt erreichen dürfen.
#                   Leer = App öffentlich erreichbar (nur hinter Entra-Login).
#                   Empfehlung, wenn die Anmeldung per Conditional Access ohnehin auf
#                   Firmen-IPs beschränkt ist: dieselben IPs hier eintragen.
#
# Das Skript ist wiederholbar. Es ändert nichts an Docker-Containern.
# =====================================================================
set -euo pipefail

ADMIN_USER="${ADMIN_USER:?ADMIN_USER setzen (euer Admin-Benutzer, nicht root)}"
SSH_ALLOW_FROM="${SSH_ALLOW_FROM:-}"
HTTPS_ALLOW_FROM="${HTTPS_ALLOW_FROM:-}"
SSH_PORT="${SSH_PORT:-22}"

if [[ $EUID -ne 0 ]]; then
  echo "Bitte mit sudo ausführen." >&2
  exit 1
fi

home_dir=$(getent passwd "$ADMIN_USER" | cut -d: -f6)
if [[ -z "$home_dir" || ! -s "$home_dir/.ssh/authorized_keys" ]]; then
  echo "Abbruch: $ADMIN_USER hat keine ~/.ssh/authorized_keys – sonst sperrst du dich aus." >&2
  exit 1
fi

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
systemctl reload ssh || systemctl reload sshd

echo "==> Firewall: alles zu außer SSH, 80 (nur Zertifikat/Weiterleitung) und 443"
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
if [[ -n "$SSH_ALLOW_FROM" ]]; then
  for src in $SSH_ALLOW_FROM; do
    ufw allow from "$src" to any port "$SSH_PORT" proto tcp comment "SSH Firma"
  done
else
  ufw limit "$SSH_PORT"/tcp comment "SSH (rate-limited)"
fi
# Port 80 bleibt offen: Let's Encrypt prüft von wechselnden Adressen aus.
# Nginx liefert dort nur die Prüfdatei und eine Weiterleitung – keine App.
ufw allow 80/tcp comment "HTTP (Let's Encrypt, Weiterleitung)"
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
echo "  * Zertifikat holen:  certbot certonly --webroot -w /var/www/certbot -d aufgaben.example.de"
echo "  * deploy/nginx/aufgaben-oeffentlich.conf einspielen, Backups einrichten (deploy/backup)"
