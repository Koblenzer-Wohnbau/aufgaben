# Aufgaben

**Aufgabenverwaltung für Microsoft Teams und den Browser – ohne Exchange Online.**

<img width="3439" height="1251" alt="image" src="https://github.com/user-attachments/assets/6bef9845-3815-4cb6-9197-569c7d08d514" />
<img width="780" height="1688" alt="image" src="https://github.com/user-attachments/assets/805a2831-dff8-4a04-ad83-057810b3d73d" />



Microsoft To Do und die persönlichen Aufgaben in Planner setzen ein Exchange-Online-Postfach voraus.
Organisationen, die ihre Postfächer noch auf einem eigenen Exchange Server betreiben, gehen dabei leer aus.
*Aufgaben* schließt diese Lücke als eigenständige, selbst gehostete App mit Teams-Integration.

* **Persönliche Aufgaben** wie in To Do: Mein Tag, Wichtig, Geplant, eigene Listen, Erinnerungen, Wiederholungen, Checklisten
* **Delegieren mit Rückmeldung**: Benachrichtigung im Teams-Aktivitätsfeed oder per E-Mail, Status live
* **Projektaufgaben** als Tab im Teams-Kanal – für alle sichtbar
* **Schnelleingabe** auf Deutsch: `Montag 10 Uhr Angebot prüfen #1001 @Becker !hoch`
* **Vorlagen** für wiederkehrende Abläufe (Beispiele: Mieterwechsel, Wohnungsabnahme, Mängelmeldung)
* **Mail an Aufgabe**: Mail an ein eigenes Postfach weiterleiten – fertig ist die Aufgabe mit Text und Anhängen
  (IMAP, sicher auch mit Exchange Server on-premises, siehe [docs/mail-an-aufgabe.md](docs/mail-an-aufgabe.md))
* **Kalender-Feed** für Outlook, **Tagesübersicht** am Morgen, **Wiedervorlagen**
* Auch für Personen **ohne Teams-Lizenz** nutzbar (Browser und E-Mail)

## Aufbau

| Teil | Technik |
|---|---|
| Backend | C# / ASP.NET Core (.NET 10 LTS), Minimal APIs |
| Datenbank | PostgreSQL 17 (EF Core, Migrationen beim Start) |
| Anmeldung | Microsoft Entra ID: OpenID Connect im Browser (Microsoft.Identity.Web), Teams-SSO-Token als Bearer |
| Microsoft Graph | App-Identität mit Zertifikat (Azure.Identity): Verzeichnisabgleich, Teams-Aktivitätsfeed |
| E-Mail | SMTP (MailKit), z. B. über einen internen Exchange-Relay; Abruf des Aufgaben-Postfachs per IMAP |
| Frontend | React 19, Fluent UI v9 (Teams-Design, hell/dunkel/Kontrast), TeamsJS v2 |
| Live-Updates | Server-Sent Events |
| Betrieb | Docker Compose, Nginx davor, „chiseled“ .NET-Image ohne Shell und ohne Root |

```
src/Aufgaben.Api/     Backend (Program.cs, Auth/, Services/, Endpoints/, Data/)
frontend/             Oberfläche (React)
tests/Aufgaben.Tests/ Unit- und API-Tests (PostgreSQL per Testcontainers)
teams/                Teams-Manifest-Vorlage, Icons, Paket-Skript
deploy/nginx/         Nginx-Beispiele (öffentlicher Server mit Let's Encrypt, internes Netz mit eigener CA)
deploy/server/        Härtungsskript für einen Ubuntu-Server (Firewall, SSH, fail2ban, Updates, Docker)
deploy/backup/        Verschlüsselte Sicherung mit restic und automatischer Wiederherstellungstest
```

## Lokal ausprobieren

Voraussetzungen: .NET 10 SDK, Node.js 22+, Docker.

```powershell
docker compose -f compose.dev.yaml up -d          # PostgreSQL auf localhost:55432
cd frontend; npm ci; npm run build; cd ..          # Oberfläche nach wwwroot bauen
dotnet run --project src/Aufgaben.Api              # http://localhost:5080
```

Im Entwicklungsmodus meldest du dich mit erfundenen Testpersonen an – ohne Entra. Dieser Login startet nur
mit einer `localhost`-Adresse und `Aufgaben:Auth:AllowDevLogin=true` und verweigert sonst den Start.

Oberfläche mit Hot Reload: zusätzlich `cd frontend; npm run dev` (Port 5173, leitet API-Aufrufe an 5080 weiter).

```powershell
dotnet test                 # Backend-Tests (braucht Docker für PostgreSQL)
cd frontend; npm test       # Tests der Schnelleingabe
```

## Betrieb

### Installation

1. Server vorbereiten: `deploy/server/haerten.sh` (Firewall, SSH nur mit Schlüssel, optional nur bestimmte IPs)
2. Repository auf den Server holen, `.env.example` nach `.env` kopieren und ausfüllen (`chmod 600 .env`)
3. App-Zertifikat als `secrets/app.pfx` ablegen (lesbar für UID 1654, den Benutzer im Container)
4. Starten: `docker compose up -d` (eigenes Image bauen: `--build`)
5. Nginx-Konfiguration aus `deploy/nginx/` einspielen, TLS-Zertifikat einrichten
6. Entra-App einrichten (Checkliste unten), Teams-Paket bauen: `teams/build-package.ps1`, im Teams Admin Center hochladen
7. Sicherung einrichten: `deploy/backup/backup.sh`, Wiederherstellung prüfen: `deploy/backup/restore-test.sh`

### Updates

Jede Version mit Tag `vX.Y.Z` wird von GitHub Actions getestet und als Image veröffentlicht
(`ghcr.io/kobi-tools/aufgaben:X.Y.Z`, mit SBOM und Herkunftsnachweis). Auf dem Server:

```bash
# in .env: APP_IMAGE=ghcr.io/kobi-tools/aufgaben  und  APP_VERSION=X.Y.Z
docker compose pull && docker compose up -d
```

Zurück auf die vorherige Version: alte Nummer in `.env` eintragen und denselben Befehl ausführen.
Vor Updates mit Datenbankänderungen vorher `deploy/backup/backup.sh` laufen lassen.

Organisationsspezifisches (Domain, Mandant, Mailserver, Zertifikate) steht **nur** in `.env` und `secrets/`
auf dem Server – nie im Repository.

## Entra-App – Checkliste

1. App-Registrierung „Aufgaben“, nur eigener Mandant
2. Authentifizierung → Plattform **Web**, Umleitungs-URI `https://<domain>/signin-oidc`
3. Zertifikate & Geheimnisse → **öffentliches** Zertifikat hochladen (der private Schlüssel bleibt auf dem Server)
4. API verfügbar machen → Application ID URI `api://<domain>/<client-id>`, Bereich `access_as_user`,
   autorisierte Client-Apps: `1fec8e78-bce4-4aaf-ab1b-5451cc387264` (Teams Desktop/Mobil) und
   `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` (Teams Web)
5. API-Berechtigungen (Microsoft Graph)
   * delegiert: `openid`, `profile`, `email`, `offline_access`, `User.Read`
   * Anwendung: `User.Read.All`, `TeamsActivity.Send` (bei Gruppenfilter zusätzlich `GroupMember.Read.All`)
   * Administratorzustimmung erteilen
6. App-Rollen → `Aufgaben.Admin` anlegen und in der Unternehmensanwendung zuweisen
7. Empfohlen: Conditional Access für die App (MFA, ggf. nur Firmen-IPs)

## Sicherheit

* Alles hinter Entra-Login; der Kalender-Feed nur mit geheimem, widerrufbarem Link (in der Datenbank nur als Hash)
* Private Aufgaben sehen nur Ersteller und Verantwortliche – auch Administratoren nicht
* CSRF-Schutz für Cookie-Sitzungen, Rate-Limiting in App und Nginx (NAT-tauglich), strikte CSP, HSTS
* Optional: Server nur für Firmen-IPs erreichbar (Firewall und Nginx), passend zu Conditional Access
* Container schreibgeschützt, ohne Root und ohne Linux-Capabilities; Datenbank ohne Internetzugang
* Keine Leistungs- oder Verhaltensauswertungen pro Person
* Sicherheitslücken bitte vertraulich melden, siehe [SECURITY.md](SECURITY.md)

## Lizenz

[MIT](LICENSE)
