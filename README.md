# Aufgaben

**Aufgabenverwaltung für Microsoft Teams und den Browser – ohne Exchange Online.**

<img width="458" height="916" alt="image" src="https://github.com/user-attachments/assets/c5a4915d-929d-4c1b-9d29-ff1c3a5f86ea" />



Microsoft To Do und die persönlichen Aufgaben in Planner setzen ein Exchange-Online-Postfach voraus.
Organisationen, die ihre Postfächer noch auf einem eigenen Exchange Server betreiben, gehen dabei leer aus.
*Aufgaben* schließt diese Lücke als eigenständige, selbst gehostete App mit Teams-Integration.

* **Persönliche Aufgaben** wie in To Do: Mein Tag mit **Vorschlägen** und Fortschrittsring, Wichtig, Geplant, Aufgaben,
  eigene und **geteilte Listen**, Erinnerungen, Wiederholungen, Checklisten
* **Ziehen und Ablegen**: Aufgabe auf „Mein Tag“, „Wichtig“, eine Liste oder ein Projekt ziehen; Sortierung nach
  Fälligkeit, Wichtigkeit, Alphabet oder Datum; Tastenkürzel `n` (neue Aufgabe), `/` (Suche), `Esc`
* **Delegieren mit Rückmeldung**: Benachrichtigung im Teams-Aktivitätsfeed oder per E-Mail, Status live
* **Projektaufgaben** als Tab im Teams-Kanal – für alle sichtbar
* **Schnelleingabe** auf Deutsch: `Montag 10 Uhr Angebot prüfen #1001 @Becker !hoch`
* **Vorlagen** für wiederkehrende Abläufe (Beispiele: Mieterwechsel, Wohnungsabnahme, Mängelmeldung)
* **Mail an Aufgabe**: Mail an ein eigenes Postfach weiterleiten – fertig ist die Aufgabe mit Text und Anhängen
  (IMAP, sicher auch mit Exchange Server on-premises, siehe [docs/mail-an-aufgabe.md](docs/mail-an-aufgabe.md))
* **Anhänge direkt an der Aufgabe**: Screenshot mit `Strg+V` einfügen, Dateien hineinziehen oder auswählen –
  bis 10 MB pro Datei, Vorschau für Bilder, Programme und Skripte gesperrt, Download statt Anzeige im Browser
* **Warte auf …** und **Später**: Aufgabe auf „Warte auf Fa. Müller“ setzen oder bis Montag zurückstellen – sie
  verschwindet aus den Listen in die **Warteschleife** und steht am Tag zum Nachhaken wieder in „Mein Tag“,
  mit Erinnerung per Teams oder E-Mail. Eine Fälligkeit holt sie immer früher zurück
* **Urlaubsübergabe**: Urlaub mit Vertretung eintragen, dann für jede Aufgabe, die im Urlaub fällig wird, entscheiden –
  an die Vertretung übergeben (mit Hinweis) oder bis nach dem Urlaub zurückstellen, auf Wunsch alles mit einem Klick.
  Die Vertretung sieht ein Banner mit den übergebenen Aufgaben; nach der Rückkehr zeigt „Mein Tag“, was passiert ist,
  und holt Offenes mit „Zurück an mich“ zurück. Im Urlaub kommt keine Tagesübersicht
* **Mehrere Aufgaben auf einmal**: „Auswählen“ mit „Alle“ und Umschalt+Klick – erledigen, später oder löschen;
  „Erledigte löschen“ räumt in einem Schritt auf
* **Papierkorb**: Gelöschtes bleibt 30 Tage wiederherstellbar und wird danach endgültig gelöscht
* **Kalender-Feed** für Outlook, **Tagesübersicht** am Morgen, **Wiedervorlagen**
* **Einsichtsrechte** mit Formular zum Drucken, Unterschriftsbestätigung, Ablaufdatum und Zugriffsprotokoll
  (Verwaltung → Einsichtsrechte, siehe [docs/einsichtsrechte.md](docs/einsichtsrechte.md))
* **Installierbar** als App (PWA) am PC und auf dem Handy
* Auch für Personen **ohne Teams-Lizenz** nutzbar (Browser und E-Mail)

## Aufbau

| Teil | Technik |
|---|---|
| Backend | C# / ASP.NET Core (.NET 10 LTS), Minimal APIs |
| Datenbank | PostgreSQL 17 (EF Core, Migrationen beim Start) |
| Anmeldung | Microsoft Entra ID: OpenID Connect im Browser (Microsoft.Identity.Web), Teams-SSO-Token als Bearer |
| Microsoft Graph | App-Identität mit Zertifikat oder Clientschlüssel (Azure.Identity): Verzeichnisabgleich, Teams-Aktivitätsfeed |
| E-Mail | SMTP (MailKit), z. B. über einen internen Exchange-Relay; Abruf des Aufgaben-Postfachs per IMAP |
| Frontend | React 19, Fluent UI v9 (Teams-Design, hell/dunkel/Kontrast), TeamsJS v2 |
| Live-Updates | Server-Sent Events |
| Betrieb | Docker Compose, Nginx davor, „chiseled“ .NET-Image ohne Shell und ohne Root |

```
src/Aufgaben.Api/     Backend (Program.cs, Auth/, Services/, Endpoints/, Data/)
frontend/             Oberfläche (React)
tests/Aufgaben.Tests/ Unit- und API-Tests (PostgreSQL per Testcontainers)
teams/                Teams-Manifest-Vorlage und Icons (in die App eingebettet), Paket-Skript
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
cd frontend; npm test       # Tests der Schnelleingabe und des Freigabe-Formulars
```

## Betrieb

### Installation

1. SSH-Zugang einrichten – Windows: `deploy/server/ssh-zugang-einrichten.ps1 -Server <IP> -User <name>`,
   Linux/macOS (z. B. Fedora): `bash deploy/server/ssh-zugang-einrichten.sh -s <IP> -u <name>`
   (erzeugt den Schlüssel, trägt ihn ein, testet den Login, legt Kurznamen wie `ssh <hostname>` an).
   Aktuelle Fassung mit Import aus der Zwischenablage: <https://github.com/zerox80/ssh-hardening>
2. Server vorbereiten: `sudo bash deploy/server/haerten.sh` (fragt alles ab: Firewall, SSH nur mit Schlüssel,
   optional nur bestimmte IPs)
3. Repository auf den Server holen, `.env.example` nach `.env` kopieren und ausfüllen (`chmod 600 .env`)
4. Anmeldung der App bei Entra: App-Zertifikat als `secrets/app.pfx` (lesbar für UID 1654, den Benutzer im
   Container) – oder übergangsweise ein Clientschlüssel (`AzureAd__ClientCredentials__0__SourceType=ClientSecret`,
   `…__ClientSecret=` mit dem **Wert**, nicht der „Geheimen ID“)
5. Starten: `docker compose pull && docker compose up -d` (selbst bauen statt herunterladen: `compose.build.yaml`)
6. TLS-Zertifikat, z. B. per DNS-Challenge über Cloudflare:
   `sudo bash -c "$(curl -fsSL https://raw.githubusercontent.com/zerox80/certbot-sexy/main/zertifikat-cloudflare.sh)"`
7. Nginx aus `deploy/nginx/` einspielen. Immer von der Vorlage ausgehen (wiederholbar), z. B.:

   ```bash
   sed -e 's/aufgaben\.example\.de/<domain>/g' \
       -e 's|^    default 0;|    default 0;\n    <internes Netz> 1;|' \
       -e '/if (\$aufgaben_firma = 0) {/,/^    }/ s/^/#/' \
       deploy/nginx/aufgaben-oeffentlich.conf | sudo tee /etc/nginx/sites-available/aufgaben.conf >/dev/null
   sudo ln -sf /etc/nginx/sites-available/aufgaben.conf /etc/nginx/sites-enabled/aufgaben.conf
   sudo nginx -t && sudo systemctl reload nginx
   ```

   Der dritte `-e`-Ausdruck schaltet die Sperre für fremde Adressen aus (öffentlich erreichbar, Schutz durch den
   Entra-Login). Nicht `sed -i` auf der fertigen Datei wiederholen – das verdoppelt Einträge.
8. Netz: Im eigenen Netz die Domain per Split-DNS auf die interne Adresse des Servers zeigen lassen. Steht der
   Server hinter einer Firewall mit Portweiterleitung (VIP), brauchen VPN-Clients eine eigene Regel vom
   VPN-Tunnel zum Server-Netz mit **dem VIP-Objekt als Ziel** (ein Adressobjekt mit derselben IP passt nicht).
9. Entra-App einrichten (Checkliste unten). Teams-Paket: in der App unter **Verwaltung → System →
   „Teams-Paket herunterladen“** (der Server baut es aus seiner `.env`), dann im Teams Admin Center hochladen.
   Ohne laufenden Server: `teams/build-package.ps1`.
10. Sicherung einrichten: `deploy/backup/backup.sh`, Wiederherstellung prüfen: `deploy/backup/restore-test.sh`

Die App nimmt Schema und Host im Betrieb immer aus `Aufgaben__BaseUrl` – die Anmeldung funktioniert dadurch
auch, wenn der Reverse Proxy `Host` oder `X-Forwarded-Proto` nicht richtig weiterreicht.

### Updates

Neue Version veröffentlichen: GitHub → **Actions → „Release“ → „Run workflow“** → Version eintragen
(z. B. `1.0.4`). Der Lauf legt den Tag an, testet, baut das Image und erstellt das Release
(`ghcr.io/koblenzer-wohnbau/aufgaben:X.Y.Z`, mit SBOM und Herkunftsnachweis). Ein von Hand gepushter Tag
`vX.Y.Z` tut dasselbe. Die Versionsnummer steht danach in der App unter Verwaltung → System.

Auf dem Server, im Installationsverzeichnis:

```bash
sudo sed -i 's/^APP_VERSION=.*/APP_VERSION=X.Y.Z/' .env && sudo docker compose pull && sudo docker compose up -d
```

Zurück auf die vorherige Version: alte Nummer in `.env` eintragen und denselben Befehl ausführen.

Ab der Version mit hochladbaren Anhängen braucht Nginx eine eigene `location` für
`/api/tasks/<id>/attachments` mit `client_max_body_size 11m` (in beiden Vorlagen unter `deploy/nginx/`
enthalten). Die Nginx-Seite deshalb einmal aus der aktuellen Vorlage neu schreiben – sonst lehnt Nginx
Dateien über 1 MB mit „413“ ab.
Vor Updates mit Datenbankänderungen vorher `deploy/backup/backup.sh` laufen lassen.

Organisationsspezifisches (Domain, Mandant, Mailserver, Zertifikate) steht **nur** in `.env` und `secrets/`
auf dem Server – nie im Repository.

## Entra-App – Checkliste

1. App-Registrierung „Aufgaben“, nur eigener Mandant
2. Authentifizierung → Plattform **Web**, Umleitungs-URI `https://<domain>/signin-oidc`
3. Zertifikate & Geheimnisse → **öffentliches** Zertifikat hochladen (der private Schlüssel bleibt auf dem Server);
   übergangsweise ein geheimer Clientschlüssel – in die `.env` gehört dessen **Wert**
4. API verfügbar machen → Application ID URI `api://<domain>/<client-id>`, Bereich `access_as_user`,
   autorisierte Client-Apps: `1fec8e78-bce4-4aaf-ab1b-5451cc387264` (Teams Desktop/Mobil) und
   `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` (Teams Web)
5. API-Berechtigungen (Microsoft Graph)
   * delegiert: `openid`, `profile`, `email`, `offline_access`, `User.Read`
   * Anwendung: `User.Read.All`, `TeamsActivity.Send` (bei Gruppenfilter zusätzlich `GroupMember.Read.All`)
   * Administratorzustimmung erteilen
6. App-Rollen → Rolle mit dem Wert `Aufgaben.Admin` (Benutzer/Gruppen) anlegen und in der Unternehmensanwendung zuweisen
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
