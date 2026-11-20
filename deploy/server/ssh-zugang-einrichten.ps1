<#
.SYNOPSIS
  Richtet den SSH-Zugang von diesem Windows-PC zum Aufgaben-Server ein – in einem Rutsch.

.DESCRIPTION
  1. Erzeugt einen eigenen SSH-Schlüssel (ed25519), falls er noch nicht existiert.
  2. Kopiert den öffentlichen Schlüssel auf den Server (fragt dafür EINMAL nach dem Server-Passwort).
  3. Prüft, ob die Anmeldung jetzt ohne Passwort klappt.
  4. Legt in ~/.ssh/config einen Kurznamen an – danach genügt "ssh aufgaben".

  Erst wenn Schritt 3 klappt, darf deploy/server/haerten.sh laufen (es schaltet Passwörter für SSH ab).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\deploy\server\ssh-zugang-einrichten.ps1 -Server 192.168.1.50 -User admin
#>
param(
    [Parameter(Mandatory)] [string] $Server,
    [Parameter(Mandatory)] [string] $User,
    [int] $Port = 22,
    [string] $Alias = "aufgaben",
    [string] $KeyName = "id_ed25519_aufgaben"
)

$ErrorActionPreference = "Stop"

function Write-Step([string] $text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }
function Stop-WithError([string] $text) { Write-Host ""; Write-Host "ABBRUCH: $text" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- OpenSSH vorhanden?
Write-Step "OpenSSH-Client prüfen"
foreach ($tool in "ssh", "ssh-keygen") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Stop-WithError "$tool fehlt. Einstellungen -> System -> Optionale Features -> 'OpenSSH-Client' hinzufügen, dann erneut starten."
    }
}
Write-Host "ok"

$sshDir = Join-Path $env:USERPROFILE ".ssh"
$key = Join-Path $sshDir $KeyName
$pub = "$key.pub"
if (-not (Test-Path $sshDir)) { New-Item -ItemType Directory -Path $sshDir | Out-Null }

# ---------------------------------------------------------------- 1. Schlüssel
Write-Step "Schlüssel $key"
if (Test-Path $pub) {
    Write-Host "vorhanden – wird wiederverwendet"
} else {
    Write-Host "Neuer Schlüssel. Eine Passphrase ist empfohlen (schützt den Schlüssel, falls jemand an den PC kommt)."
    Write-Host "Ohne Passphrase: zweimal Enter drücken."
    & ssh-keygen -t ed25519 -a 64 -f $key -C "$env:USERNAME@$env:COMPUTERNAME aufgaben"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $pub)) { Stop-WithError "Schlüssel konnte nicht erzeugt werden." }
}
$publicKey = (Get-Content $pub -Raw).Trim()
& ssh-keygen -lf $pub

# ---------------------------------------------------------------- 2. Auf den Server kopieren
Write-Step "Öffentlichen Schlüssel auf ${User}@${Server} eintragen"
Write-Host "Beim ersten Verbinden wird der Server-Fingerabdruck gespeichert."
Write-Host "Jetzt das PASSWORT des Server-Benutzers eingeben (nur dieses eine Mal):"
# Ohne doppelte Anführungszeichen, weil Windows PowerShell 5.1 sie an Programme nicht sauber weiterreicht.
# Der Schlüssel kommt über die Standardeingabe; Windows-Zeilenenden werden entfernt, doppelte Einträge vermieden.
$remote = 'umask 077; mkdir -p ~/.ssh; tr -d ''\r'' > ~/.ssh/.aufgaben-neu; touch ~/.ssh/authorized_keys; ' +
          'grep -qxFf ~/.ssh/.aufgaben-neu ~/.ssh/authorized_keys || cat ~/.ssh/.aufgaben-neu >> ~/.ssh/authorized_keys; ' +
          'rm -f ~/.ssh/.aufgaben-neu; chmod 700 ~/.ssh; chmod 600 ~/.ssh/authorized_keys; echo eingetragen'
$publicKey | & ssh -p $Port -o StrictHostKeyChecking=accept-new -o PubkeyAuthentication=no "$User@$Server" $remote
if ($LASTEXITCODE -ne 0) { Stop-WithError "Eintragen fehlgeschlagen (Passwort, Benutzer oder Netzwerk prüfen)." }

# ---------------------------------------------------------------- 3. Testen
Write-Step "Anmeldung ohne Passwort testen"
$check = & ssh -p $Port -i $key -o IdentitiesOnly=yes -o PasswordAuthentication=no -o KbdInteractiveAuthentication=no "$User@$Server" "echo schluessel-ok"
if ($LASTEXITCODE -ne 0 -or ($check -notcontains "schluessel-ok")) {
    Stop-WithError "Anmeldung mit Schlüssel klappt noch nicht. haerten.sh NICHT ausführen."
}
Write-Host "Anmeldung mit Schlüssel klappt." -ForegroundColor Green

# ---------------------------------------------------------------- 4. Kurzname in ~/.ssh/config
Write-Step "Kurzname '$Alias' in $sshDir\config"
$config = Join-Path $sshDir "config"
$existing = ""
if (Test-Path $config) { $existing = Get-Content $config -Raw }
if ($existing -match "(?m)^\s*Host\s+$([regex]::Escape($Alias))\s*$") {
    Write-Host "Eintrag '$Alias' existiert schon – nicht verändert. Bei Bedarf $config von Hand anpassen."
} else {
    $block = @(
        "",
        "Host $Alias",
        "    HostName $Server",
        "    User $User",
        "    Port $Port",
        "    IdentityFile $key",
        "    IdentitiesOnly yes",
        "    ServerAliveInterval 60",
        ""
    ) -join "`n"
    [System.IO.File]::AppendAllText($config, $block, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "angelegt"
}

# ---------------------------------------------------------------- Fertig
Write-Host ""
Write-Host "Fertig. Ab jetzt:  ssh $Alias" -ForegroundColor Green
Write-Host ""
Write-Host "Optional (Fingerabdruck vergleichen): in der vSphere-Konsole der VM"
Write-Host "  ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub"
Write-Host "und mit dem Eintrag für $Server in $sshDir\known_hosts vergleichen:"
Write-Host "  ssh-keygen -lF $Server"
Write-Host ""
Write-Host "Nächster Schritt auf dem Server: deploy/server/haerten.sh mit ADMIN_USER=$User (siehe README)."
