#!/usr/bin/env bash
# =====================================================================
# Richtet den SSH-Zugang von diesem Linux- oder macOS-Rechner (z. B. Fedora) zum
# Aufgaben-Server ein – Gegenstück zu ssh-zugang-einrichten.ps1 für Windows.
#
#   1. Erzeugt einen eigenen SSH-Schlüssel (ed25519), falls er noch nicht existiert.
#   2. Kopiert den öffentlichen Schlüssel auf den Server (fragt dafür EINMAL nach dem Passwort).
#   3. Prüft, ob die Anmeldung jetzt ohne Passwort klappt.
#   4. Legt in ~/.ssh/config Kurznamen an – danach genügt "ssh <hostname der VM>".
#
# Aufruf:
#   bash deploy/server/ssh-zugang-einrichten.sh -s 192.168.1.50 -u itadmin
#   Optionen: -p <port> (Standard 22)  -a <kurzname> (Standard aufgaben)
#
# Erst wenn Schritt 3 klappt, darf deploy/server/haerten.sh laufen (es schaltet Passwörter für SSH ab).
# =====================================================================
set -euo pipefail

server=""
user_name=""
port=22
alias_name="aufgaben"
key_name="id_ed25519_aufgaben"

usage() {
  echo "Aufruf: $0 -s <server-ip-oder-name> -u <benutzer> [-p <port>] [-a <kurzname>]" >&2
  exit 2
}

while getopts "s:u:p:a:h" opt; do
  case "$opt" in
    s) server="$OPTARG" ;;
    u) user_name="$OPTARG" ;;
    p) port="$OPTARG" ;;
    a) alias_name="$OPTARG" ;;
    *) usage ;;
  esac
done
[[ -n "$server" && -n "$user_name" ]] || usage

step() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }
fail() { printf '\n\033[31mABBRUCH: %s\033[0m\n' "$1" >&2; exit 1; }

# ---------------------------------------------------------------- OpenSSH vorhanden?
step "OpenSSH-Client prüfen"
for tool in ssh ssh-keygen; do
  command -v "$tool" >/dev/null 2>&1 || fail "$tool fehlt. Fedora: sudo dnf install -y openssh-clients"
done
echo "ok"

ssh_dir="$HOME/.ssh"
key="$ssh_dir/$key_name"
mkdir -p "$ssh_dir"
chmod 700 "$ssh_dir"

# ---------------------------------------------------------------- 1. Schlüssel
step "Schlüssel $key"
if [[ -f "$key.pub" ]]; then
  echo "vorhanden – wird wiederverwendet"
else
  echo "Neuer Schlüssel. Eine Passphrase ist empfohlen (schützt den Schlüssel, falls jemand an den Rechner kommt)."
  echo "Ohne Passphrase: zweimal Enter drücken."
  ssh-keygen -t ed25519 -a 64 -f "$key" -C "$(id -un)@${HOSTNAME%%.*} aufgaben"
  [[ -f "$key.pub" ]] || fail "Schlüssel konnte nicht erzeugt werden."
fi
ssh-keygen -lf "$key.pub"

# ---------------------------------------------------------------- 2. Auf den Server kopieren
step "Öffentlichen Schlüssel auf ${user_name}@${server} eintragen"
echo "Beim ersten Verbinden wird der Server-Fingerabdruck gespeichert."
echo "Jetzt das PASSWORT des Server-Benutzers eingeben (nur dieses eine Mal):"
remote='umask 077; mkdir -p ~/.ssh; tr -d '"'"'\r'"'"' > ~/.ssh/.aufgaben-neu; touch ~/.ssh/authorized_keys; '
remote+='grep -qxFf ~/.ssh/.aufgaben-neu ~/.ssh/authorized_keys || cat ~/.ssh/.aufgaben-neu >> ~/.ssh/authorized_keys; '
remote+='rm -f ~/.ssh/.aufgaben-neu; chmod 700 ~/.ssh; chmod 600 ~/.ssh/authorized_keys; echo eingetragen'
ssh -p "$port" -o StrictHostKeyChecking=accept-new -o PubkeyAuthentication=no \
  "$user_name@$server" "$remote" < "$key.pub" \
  || fail "Eintragen fehlgeschlagen (Passwort, Benutzer oder Netzwerk prüfen)."

# ---------------------------------------------------------------- 3. Testen
step "Anmeldung ohne Passwort testen"
out=$(ssh -p "$port" -i "$key" -o IdentitiesOnly=yes -o PasswordAuthentication=no \
  -o KbdInteractiveAuthentication=no "$user_name@$server" 'echo schluessel-ok; hostname -s 2>/dev/null || uname -n') \
  || fail "Anmeldung mit Schlüssel klappt noch nicht. haerten.sh NICHT ausführen."
grep -qx "schluessel-ok" <<<"$out" || fail "Anmeldung mit Schlüssel klappt noch nicht. haerten.sh NICHT ausführen."
printf '\033[32mAnmeldung mit Schlüssel klappt.\033[0m\n'
# Echter Hostname der VM – damit auch "ssh <hostname>" funktioniert, ganz ohne DNS-Eintrag.
remote_host=$(grep -vx "schluessel-ok" <<<"$out" | tail -n 1 | tr -d '[:space:]')

# ---------------------------------------------------------------- 4. Kurznamen in ~/.ssh/config
names=()
for n in "$alias_name" "$remote_host" "$server"; do
  [[ -n "$n" && "$n" =~ ^[A-Za-z0-9._-]+$ ]] || continue
  [[ " ${names[*]-} " == *" $n "* ]] || names+=("$n")
done
step "Kurznamen in $ssh_dir/config: ${names[*]}"
config="$ssh_dir/config"
touch "$config"
chmod 600 "$config"
taken=()
for n in "${names[@]}"; do
  pattern=$(printf '%s' "$n" | sed 's/[.]/\\./g')
  if grep -Eq "^[[:space:]]*Host[[:space:]]+(.*[[:space:]])?${pattern}([[:space:]]|$)" "$config"; then
    taken+=("$n")
  fi
done
if (( ${#taken[@]} > 0 )); then
  echo "Schon in der Datei vorhanden: ${taken[*]} – nicht verändert. Bei Bedarf $config von Hand anpassen."
else
  {
    echo ""
    echo "Host ${names[*]}"
    echo "    HostName $server"
    echo "    User $user_name"
    echo "    Port $port"
    echo "    IdentityFile $key"
    echo "    IdentitiesOnly yes"
    echo "    ServerAliveInterval 60"
  } >> "$config"
  echo "angelegt"
fi

# ---------------------------------------------------------------- Fertig
echo ""
for n in "${names[@]}"; do printf '\033[32mFertig. Ab jetzt:  ssh %s\033[0m\n' "$n"; done
echo ""
echo "Optional (Fingerabdruck vergleichen): in der vSphere-Konsole der VM"
echo "  ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub"
echo "und mit dem gespeicherten Eintrag vergleichen:  ssh-keygen -lF $server"
echo ""
echo "Mit Passphrase: einmal pro Sitzung 'ssh-add $key' (auf Fedora/GNOME erledigt das meist der Schlüsselbund)."
echo "Nächster Schritt auf dem Server: deploy/server/haerten.sh mit ADMIN_USER=$user_name (siehe README)."
