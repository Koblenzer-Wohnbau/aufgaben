# E-Mail senden

Die App verschickt E-Mails für Benachrichtigungen (zugewiesen, erledigt, kommentiert, Erinnerungen,
Tagesübersicht) an alle, die E-Mail statt Teams gewählt haben oder kein Teams nutzen – und die Bestätigungen
von [Mail an Aufgabe](mail-an-aufgabe.md). Eingestellt wird das in der `.env` unter `Aufgaben__Mail__*`.

## Welcher Server, welcher Port?

| Weg | Port | Anmeldung | Hinweis |
|---|---|---|---|
| **Exchange-Server direkt** (Empfangsconnector „Client Frontend“) | **587**, StartTls | Konto mit Postfach | **empfohlen** bei Exchange on-premises – so verschicken auch Outlook-Clients und Geräte |
| Exchange-Server, Port 25 („Default Frontend“) | 25 | **keine** (`Username` leer) | nur an interne Empfänger; normale Konten können sich hier meist **nicht** anmelden (`535 5.7.3`) |
| Mail-Gateway vor Exchange (z. B. FortiMail, Spamfilter) | 25 | meist keine Benutzerkonten | nur, wenn das Gateway die IP des Aufgaben-Servers als Relay zulässt |

Ein Name wie `mail.example.de` kann je nach Netz auf verschiedene Ziele zeigen: von außen oft auf eine Firewall,
die nach Port weiterleitet (z. B. 25 → Gateway, 587 → Exchange), intern direkt auf einen Server. Für den
Aufgaben-Server zählt, **was von dort aus** erreichbar ist (siehe „Prüfen“). Steht er im selben Netz wie
Exchange, ist der Exchange-Server direkt der einfachste Weg – ohne Firewall dazwischen.

## Einrichtung mit Exchange on-premises

```ini
Aufgaben__Mail__Host=exchange.example.de        # der Exchange-Server selbst, nicht das Gateway
Aufgaben__Mail__Port=587
Aufgaben__Mail__Security=StartTls
Aufgaben__Mail__Username=FIRMA\konto            # oder konto@example.de
Aufgaben__Mail__Password=<Passwort des Kontos>
Aufgaben__Mail__From=konto@example.de
Aufgaben__Mail__FromName=Aufgaben
Aufgaben__Mail__CertificateThumbprint=          # nur bei selbst ausgestelltem Zertifikat, siehe unten
```

* Das Konto braucht ein Postfach und das Recht, mit der Adresse unter `From` zu senden (eigene Adresse oder
  „Senden als“).
* Nimmt man dafür das Postfach von Mail an Aufgabe (`aufgaben@`), landen Antworten auf Bestätigungen dort – und
  werden wie weitergeleitete Mails zu Aufgaben. Wer das nicht möchte, nimmt ein eigenes Konto zum Senden.
* Die App meldet sich mit Benutzername und Passwort an (LOGIN bzw. PLAIN), sobald die Verbindung verschlüsselt
  ist – wie Outlook oder andere Programme. NTLM und Kerberos (GSSAPI), die Exchange zusätzlich anbietet, nutzt
  sie nur, wenn der Server nichts anderes anbietet.
* Die `.env` wird nur beim Start gelesen: nach Änderungen `docker compose up -d`.

## Selbst ausgestelltes Zertifikat: Fingerabdruck

Auf Port 587 (und für IMAP auf 993) zeigt Exchange oft nur sein Standardzertifikat, ausgestellt auf den
internen Servernamen (z. B. `server.firma.local`). Die App lehnt die Verbindung dann zunächst ab. Statt einer
CA-Datei genügt der Fingerabdruck **genau dieses** Zertifikats; die App akzeptiert dann nur dieses eine:

```bash
echo | openssl s_client -connect exchange.example.de:587 -starttls smtp 2>/dev/null \
  | openssl x509 -noout -subject -fingerprint -sha256
```

```ini
Aufgaben__Mail__CertificateThumbprint=AB:CD:…   # Ausgabe von oben, Doppelpunkte erlaubt
```

* Senden (`Aufgaben__Mail__…`) und Abholen (`Aufgaben__MailInbox__…`) haben **getrennte** Einstellungen. Den
  Fingerabdruck nur dort eintragen, wo der Server dieses Zertifikat auch zeigt: Ein Fingerabdruck beim falschen
  Server (z. B. einem Gateway mit öffentlichem Zertifikat) lässt jede Verbindung scheitern.
* Zeigt der Server ein gültiges öffentliches Zertifikat, bleibt die Zeile leer.
* Wird das Exchange-Zertifikat erneuert, ändert sich der Fingerabdruck – dann den neuen eintragen. Das Ablaufdatum
  zeigt `openssl x509 -noout -enddate`.

## Prüfen – erst ohne Passwort

Jeder fehlgeschlagene Anmeldeversuch zählt im Active Directory als Fehlanmeldung; viele davon sperren das Konto.
Deshalb zuerst Erreichbarkeit, Anmeldeverfahren und Zertifikat prüfen, auf dem Aufgaben-Server:

```bash
# Erreichbar? „offen“ = gut, sofort „Connection refused“ = Port zu, keine Antwort = Firewall verwirft
timeout 5 bash -c '</dev/tcp/exchange.example.de/587' && echo offen

# Angebotene Anmeldeverfahren nach STARTTLS (Zeile „250-AUTH …“)
(sleep 2; printf 'EHLO test\r\n'; sleep 2; printf 'QUIT\r\n') \
  | openssl s_client -connect exchange.example.de:587 -starttls smtp -quiet 2>/dev/null | grep AUTH
```

Danach eine Benachrichtigung auslösen (z. B. jemandem mit E-Mail-Benachrichtigung eine Aufgabe zuweisen) und ins
Log schauen: `docker compose logs app --since 5m`.

## Fehlerbilder

| Im Log | Ursache | Lösung |
|---|---|---|
| `535 5.7.0 authentication failed` | Ein Gateway nimmt die Verbindung an und kennt das Konto nicht | Exchange direkt auf 587 verwenden |
| `535 5.7.3 Authentication unsuccessful` | Exchange lehnt ab: Port 25 (keine Anmeldung für Benutzer), falsches Passwort, Konto gesperrt oder abgelaufen | Port 587; Konto prüfen: `Get-ADUser <konto> -Properties LockedOut,PasswordExpired` |
| Zeitüberschreitung beim Verbinden | Firewall verwirft, z. B. zwischen Netzsegmenten oder an einer Portweiterleitung (VIP) | direkten Weg im selben Netz nehmen oder Regel für die IP des Aufgaben-Servers freigeben; das Firewall-Log zeigt „Deny“ |
| Zertifikat abgelehnt (`RemoteCertificateNameMismatch`, `RemoteCertificateChainErrors`) | selbst ausgestelltes oder internes Zertifikat | Fingerabdruck eintragen (oben) oder `CaCertificatePath` mit der Firmen-CA |
| `Cannot load library libgssapi_krb5.so.2` beim Start | harmlos: Kerberos-Bibliothek fehlt im Container, die App braucht sie nicht | nichts |
