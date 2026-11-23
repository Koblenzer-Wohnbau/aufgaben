<#
.SYNOPSIS
  Richtet den SSH-Zugang von diesem Windows-PC zum Aufgaben-Server ein, in einem Rutsch.

.DESCRIPTION
  1. Erzeugt einen eigenen SSH-Schlüssel (ed25519), falls er noch nicht existiert.
  2. Kopiert den öffentlichen Schlüssel auf den Server (fragt dafür EINMAL nach dem Server-Passwort).
  3. Prüft, ob die Anmeldung jetzt ohne Passwort klappt.
  4. Legt in ~/.ssh/config Kurznamen an, danach genügt "ssh <hostname der VM>" (oder "ssh aufgaben").

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

# Windows-OpenSSH verweigert private Schlüssel und ~/.ssh/config, wenn außer dem Besitzer noch jemand
# Rechte hat: häufig vom Profilordner geerbt (z. B. ein Support-Konto): "Bad owner or permissions" bzw.
# "UNPROTECTED PRIVATE KEY FILE". Deshalb: nur die eigene Person und SYSTEM, keine Vererbung.
function Set-OwnerOnlyAcl([string] $path) {
    # icacls statt Set-Acl: braucht keine Sonderrechte (Set-Acl verlangt je nach Datei SeSecurityPrivilege),
    # gibt es auf jedem Windows und arbeitet mit SIDs: unabhängig von der Sprache des Systems.
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & icacls $path /reset /Q 2>$null | Out-Null
        & icacls $path /inheritance:r /grant:r "*${sid}:F" "*S-1-5-18:F" /Q 2>$null | Out-Null
        $ok = ($LASTEXITCODE -eq 0)
        # Besitz übernehmen klappt nicht immer (z. B. als Administrator angelegt): OpenSSH akzeptiert
        # dann auch die Gruppe Administratoren als Besitzer.
        & icacls $path /setowner "*$sid" /Q 2>$null | Out-Null
    } finally {
        $ErrorActionPreference = $previous
    }
    if (-not $ok) {
        Stop-WithError "Rechte für $path ließen sich nicht setzen. Von Hand: icacls `"$path`" /inheritance:r /grant:r `"$($env:USERNAME):F`""
    }
}

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
    Write-Host "vorhanden, wird wiederverwendet"
} else {
    Write-Host "Neuer Schlüssel. Eine Passphrase ist empfohlen (schützt den Schlüssel, falls jemand an den PC kommt)."
    Write-Host "Ohne Passphrase: zweimal Enter drücken."
    & ssh-keygen -t ed25519 -a 64 -f $key -C "$env:USERNAME@$env:COMPUTERNAME aufgaben"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $pub)) { Stop-WithError "Schlüssel konnte nicht erzeugt werden." }
}
Set-OwnerOnlyAcl $key
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
if ($LASTEXITCODE -ne 0) {
    Stop-WithError ("Eintragen fehlgeschlagen (Passwort, Benutzer oder Netzwerk prüfen). " +
        "Meldet ssh 'REMOTE HOST IDENTIFICATION HAS CHANGED' (z. B. weil die VM neu installiert wurde): " +
        "alten Eintrag mit  ssh-keygen -R $Server  entfernen und das Skript erneut starten.")
}

# ---------------------------------------------------------------- 3. Testen
Write-Step "Anmeldung ohne Passwort testen"
$check = & ssh -p $Port -i $key -o IdentitiesOnly=yes -o PasswordAuthentication=no -o KbdInteractiveAuthentication=no "$User@$Server" "echo schluessel-ok; hostname -s"
if ($LASTEXITCODE -ne 0 -or ($check -notcontains "schluessel-ok")) {
    Stop-WithError "Anmeldung mit Schlüssel klappt noch nicht. haerten.sh NICHT ausführen."
}
Write-Host "Anmeldung mit Schlüssel klappt." -ForegroundColor Green
# Echter Hostname der VM, damit auch "ssh <hostname>" funktioniert, ganz ohne DNS-Eintrag.
$remoteHost = ($check | Where-Object { $_ -and $_ -ne "schluessel-ok" } | Select-Object -Last 1)
if ($remoteHost) { $remoteHost = $remoteHost.Trim() }

# ---------------------------------------------------------------- 4. Kurzname in ~/.ssh/config
$names = @($Alias, $remoteHost, $Server) | Where-Object { $_ -and $_ -match '^[A-Za-z0-9._-]+$' } | Select-Object -Unique
Write-Step "Kurznamen in $sshDir\config: $($names -join ', ')"
$config = Join-Path $sshDir "config"
$existing = ""
if (Test-Path $config) { $existing = Get-Content $config -Raw }
$taken = $names | Where-Object { $existing -match "(?m)^\s*Host\s+(.*\s)?$([regex]::Escape($_))(\s|$)" }
if ($taken) {
    Write-Host "Schon in der Datei vorhanden: $($taken -join ', '), nicht verändert. Bei Bedarf $config von Hand anpassen."
} else {
    $block = @(
        "",
        "Host $($names -join ' ')",
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
Set-OwnerOnlyAcl $config
& ssh -G $Alias 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Stop-WithError "ssh kann $config nicht lesen, Rechte der Datei prüfen." }

# ---------------------------------------------------------------- Fertig
Write-Host ""
foreach ($n in $names) { Write-Host "Fertig. Ab jetzt:  ssh $n" -ForegroundColor Green }
Write-Host ""
Write-Host "Optional (Fingerabdruck vergleichen): in der vSphere-Konsole der VM"
Write-Host "  ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub"
Write-Host "und mit dem Eintrag für $Server in $sshDir\known_hosts vergleichen:"
Write-Host "  ssh-keygen -lF $Server"
Write-Host ""
Write-Host "Nächster Schritt auf dem Server: deploy/server/haerten.sh mit ADMIN_USER=$User (siehe README)."
