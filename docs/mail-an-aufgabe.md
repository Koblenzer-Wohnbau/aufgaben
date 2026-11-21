# Mail an Aufgabe

Mitarbeitende leiten eine E-Mail an ein eigenes Postfach weiter, z. B. `aufgaben@example.de`.
Die App holt das Postfach per IMAP ab und macht aus jeder Mail eine Aufgabe – mit Text und Anhängen.

## So funktioniert es für Mitarbeitende

* Mail in Outlook **weiterleiten** an die Aufgaben-Adresse (die Adresse steht in der App unter *Einstellungen*).
* Der **Betreff** wird zum Titel. „WG:“, „AW:“, „FW:“ usw. werden entfernt.
* Im Betreff funktioniert dasselbe wie in der Schnelleingabe:

| Im Betreff | Wirkung |
|---|---|
| `#1001` | Projekt mit Nummer bzw. Namensanfang „1001“ |
| `@Becker` | an Thomas Becker delegieren (wird benachrichtigt) |
| `!hoch` | Priorität hoch |
| `wv` | als Wiedervorlage anlegen |
| `heute`, `morgen`, `übermorgen`, `30.11.`, `15.01.2027` | Fälligkeit |

Beispiel: `WG: Angebot Dachdecker prüfen #1001 @Becker !hoch morgen`

* Der Text der Mail landet in den Notizen, Anhänge (auch als Anhang weitergeleitete Mails als `.eml`) an der Aufgabe.
* Die absendende Person bekommt eine kurze Bestätigung mit Link (abschaltbar).

## Sicherheit

* Aufgaben entstehen **nur** aus Mails von aktiven, bekannten Personen (Absender = E-Mail oder UPN aus Entra).
* Standard (`SenderCheck=exchange-intern`): Nur Mails, die Exchange beim Empfang als intern authentifiziert
  gestempelt hat (`X-MS-Exchange-Organization-AuthAs: Internal`). Gefälschte externe Absender werden abgelehnt.
* Zusätzlich sollte das Postfach serverseitig **nur interne Absender** annehmen (siehe unten).
* Automatische Mails (Abwesenheitsnotizen, Zustellberichte) werden ignoriert, abgelehnte Absender bekommen **keine** Antwort.
* Jede Mail wird genau einmal verarbeitet. Verarbeitete Mails landen im Ordner `Verarbeitet`, abgelehnte in `Abgelehnt`.
* Anhänge werden nur als Download ausgeliefert (nie im Browser angezeigt) und nur an Personen, die die Aufgabe sehen dürfen.
* Grenze: standardmäßig 15 MB Anhänge pro Mail (`MaxAttachmentMb`); größere werden in den Notizen vermerkt.

## Einrichtung auf Exchange Server (on-premises)

Die Befehle in der Exchange-Verwaltungsshell ausführen. Namen und Adressen anpassen.

1. **Postfach anlegen** (eigenes Konto, langes zufälliges Passwort, keine Anmeldung am Windows-Desktop nötig):

   ```powershell
   New-Mailbox -Name "Aufgaben" -Alias aufgaben -UserPrincipalName aufgaben@example.de `
     -PrimarySmtpAddress aufgaben@example.de -Password (Read-Host -AsSecureString "Passwort")
   ```

2. **Nur interne Absender zulassen** – externe Mails werden schon beim Empfang abgewiesen:

   ```powershell
   Set-Mailbox aufgaben -RequireSenderAuthenticationEnabled $true
   ```

3. **IMAP aktivieren** (falls bei euch noch aus) und mit TLS betreiben:

   ```powershell
   Set-Service MSExchangeIMAP4 -StartupType Automatic; Start-Service MSExchangeIMAP4
   Set-Service MSExchangeIMAP4BE -StartupType Automatic; Start-Service MSExchangeIMAP4BE
   Set-ImapSettings -LoginType SecureLogin -X509CertificateName mail.example.de
   Restart-Service MSExchangeIMAP4, MSExchangeIMAP4BE
   Set-CASMailbox aufgaben -ImapEnabled $true
   ```

   IMAP muss nicht für alle Postfächer freigegeben sein – wer IMAP sonst nicht nutzt, kann es pro Postfach
   abschalten (`Set-CASMailbox <name> -ImapEnabled $false`). Vorher prüfen, ob Geräte oder Programme IMAP brauchen.

4. **Firewall**: Port 993 des Exchange-Servers nur für die IP des Aufgaben-Servers öffnen.
   Läuft die App außerhalb des Firmennetzes (z. B. gemieteter Server), gilt das für die öffentliche Adresse
   bzw. den veröffentlichten IMAP-Endpunkt.

5. **Aufbewahrung**: Für die Ordner `Verarbeitet` und `Abgelehnt` eine Aufbewahrungsrichtlinie setzen
   (z. B. Löschen nach 90 Tagen) – der Inhalt steckt ja schon in den Aufgaben.

## Konfiguration der App (`.env`)

```bash
Aufgaben__MailInbox__Enabled=true
Aufgaben__MailInbox__Address=aufgaben@example.de
Aufgaben__MailInbox__Host=mail.example.de
Aufgaben__MailInbox__Port=993
Aufgaben__MailInbox__Security=SslOnConnect
Aufgaben__MailInbox__Username=aufgaben@example.de
Aufgaben__MailInbox__Password=<Passwort des Postfachs>
# Bei internem Zertifikat des Mailservers: PEM der Firmen-CA (im Container z. B. unter /certs)
Aufgaben__MailInbox__CaCertificatePath=
Aufgaben__MailInbox__SenderCheck=exchange-intern
Aufgaben__MailInbox__SendConfirmation=true
```

Die Bestätigungsmails laufen über die SMTP-Einstellungen unter `Aufgaben__Mail__*`.

**Senden mit Anmeldung an Exchange:** Port **587** des Exchange-Servers direkt verwenden (Empfangsconnector
„Client Frontend“), nicht Port 25 und nicht ein vorgeschaltetes Mail-Gateway – dort scheitert die Anmeldung
normaler Konten meist mit `535`. Auf 587 zeigt Exchange oft ebenfalls sein Standardzertifikat; dann denselben
Fingerabdruck wie beim Abholen eintragen. Die App meldet sich mit Benutzername und Passwort an (LOGIN),
auch wenn Exchange zusätzlich NTLM oder Kerberos anbietet.

```ini
Aufgaben__Mail__Host=mail.example.de
Aufgaben__Mail__Port=587
Aufgaben__Mail__Security=StartTls
Aufgaben__Mail__Username=FIRMA\konto
Aufgaben__Mail__CertificateThumbprint=AB:CD:…
```

**Selbst ausgestelltes Exchange-Zertifikat:** Zeigt Exchange für IMAP nur sein Standardzertifikat
(ausgestellt auf den Servernamen, z. B. `server.firma.local`), lehnt die App die Verbindung zunächst ab.
Statt einer CA-Datei genügt dann der Fingerabdruck genau dieses Zertifikats – die App akzeptiert nur
dieses eine Zertifikat, auch unter einem anderen Namen:

```bash
echo | openssl s_client -connect mail.example.de:993 2>/dev/null | openssl x509 -noout -fingerprint -sha256
```

```ini
Aufgaben__MailInbox__CertificateThumbprint=AB:CD:…   # Ausgabe von oben, Doppelpunkte erlaubt
```

Wird das Exchange-Zertifikat erneuert, ändert sich der Fingerabdruck – dann den neuen eintragen.
Sauberer ist ein Zertifikat einer öffentlichen oder der eigenen Zertifizierungsstelle am Exchange.

## Testen

1. Als bekannte Person eine Mail mit Betreff `Test Mail an Aufgabe morgen` an die Adresse schicken.
2. Bis zu einer Minute warten – oder als Verwaltung sofort abrufen: `POST /api/admin/mail-inbox/poll`.
3. Die Aufgabe erscheint in *Mein Tag* bzw. *Geplant*; die Mail liegt im Ordner `Verarbeitet`.
4. Landet die Mail in `Abgelehnt`, steht der Grund im Log (`docker compose logs app`). Verwaltung → System zeigt
   nur die Zahl der abgelehnten Mails der letzten 7 Tage und das Ergebnis des letzten Abrufs.

**Hinweis zur Absenderprüfung:** Die Kopfzeile `X-MS-Exchange-Organization-AuthAs` wird von Exchange
gesetzt. Sollte sie in eurer Umgebung beim Abruf per IMAP fehlen (alle Mails landen mit „Nicht als interne
Mail authentifiziert“ in `Abgelehnt`), `SenderCheck=bekannte-absender` setzen. Dann ist Schritt 2
(`RequireSenderAuthenticationEnabled`) **Pflicht**, weil er gefälschte externe Absender abfängt.

## Andere Mailserver

Jeder IMAP-Server funktioniert. Ohne Exchange `SenderCheck=bekannte-absender` verwenden und am Mailserver
sicherstellen, dass das Postfach nur Mails von authentifizierten internen Absendern annimmt.
Exchange Online verlangt für IMAP OAuth – das wird derzeit nicht unterstützt.
