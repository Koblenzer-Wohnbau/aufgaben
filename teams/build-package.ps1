<#
.SYNOPSIS
  Baut das Teams-App-Paket (ZIP) für das Teams Admin Center.

.EXAMPLE
  ./teams/build-package.ps1 -BaseUrl https://aufgaben.example.de -ClientId 11111111-2222-3333-4444-555555555555 -Company "Beispiel GmbH"

  Beim ersten Mal wird eine neue App-ID erzeugt und ausgegeben. Diese ID in .env als
  Aufgaben__Teams__AppId eintragen und bei jedem weiteren Paket mit -AppId wiederverwenden,
  sonst hält Teams das Paket für eine neue App.
#>
param(
    [Parameter(Mandatory)] [string] $BaseUrl,
    [Parameter(Mandatory)] [string] $ClientId,
    [Parameter(Mandatory)] [string] $Company,
    [string] $AppId = "",
    [string] $Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$BaseUrl = $BaseUrl.TrimEnd("/")

if (-not $BaseUrl.StartsWith("https://")) { throw "BaseUrl muss mit https:// beginnen (Teams verlangt HTTPS)." }
$domain = ([Uri]$BaseUrl).Host
if ($domain -match '^\d{1,3}(\.\d{1,3}){3}$') { throw "Teams braucht einen DNS-Namen, keine IP-Adresse ($domain)." }
if (-not ($ClientId -as [guid])) { throw "ClientId ist keine GUID." }
if (-not $AppId) {
    $AppId = [guid]::NewGuid().ToString()
    Write-Host "Neue Teams-App-ID: $AppId  (in .env als Aufgaben__Teams__AppId eintragen!)" -ForegroundColor Yellow
}

$template = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot "manifest.template.json")
$manifest = $template.
    Replace("{{APP_ID}}", $AppId).
    Replace("{{CLIENT_ID}}", $ClientId).
    Replace("{{BASE_URL}}", $BaseUrl).
    Replace("{{DOMAIN}}", $domain).
    Replace("{{COMPANY}}", $Company).
    Replace("{{VERSION}}", $Version)

$null = $manifest | ConvertFrom-Json  # prüft, ob gültiges JSON entstanden ist

$out = Join-Path $root "dist\teams"
New-Item -ItemType Directory -Force $out | Out-Null
[System.IO.File]::WriteAllText((Join-Path $out "manifest.json"), $manifest, (New-Object System.Text.UTF8Encoding $false))
Copy-Item (Join-Path $PSScriptRoot "color.png") $out -Force
Copy-Item (Join-Path $PSScriptRoot "outline.png") $out -Force

$zip = Join-Path $root "dist\aufgaben-teams-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Teams-Paket erstellt: $zip" -ForegroundColor Green
Write-Host "Hochladen: Teams Admin Center → Teams-Apps → Apps verwalten → Hochladen"
