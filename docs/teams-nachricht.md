# „Als Aufgabe“ direkt aus einer Teams-Nachricht

An jeder Nachricht in Teams (Einzelchat, Gruppenchat oder Kanal) steht im Menü **„…“ → Weitere Aktionen** der
Eintrag **„Als Aufgabe“**. Es öffnet sich ein Fenster der Aufgaben-App mit dem Text der Nachricht:

* **Mit KI** (`Aufgaben__Mistral__Enabled=true`): Die KI schlägt sofort Aufgaben mit Person, Frist und Projekt vor
  und fragt bei Unklarem nach, wie bei „Mit KI erfassen“. Sie weiß, wer die Nachricht geschrieben hat; bei
  „Kannst du …“ ist also die Person gemeint, die die Nachricht als Aufgabe übernimmt.
* **Ohne KI**: ein einfaches Formular, Titel aus der ersten Zeile, die Nachricht in den Notizen.

Angelegt wird erst nach Bestätigung, mit der Anmeldung der Person. Es gelten dieselben Rechte wie in der App.
Der Link zur Nachricht steht danach in den Notizen der Aufgabe. Übernommen wird der Text; Bilder in der Nachricht
noch nicht (dafür ein Bildschirmfoto mit Strg+V in „Mit KI erfassen“ einfügen).

## Einrichtung

Teams erlaubt Einträge im „…“-Menü nur über einen **Bot**. Die Aufgaben-App nutzt ihn ausschließlich als
Empfänger: Sie antwortet Teams direkt, schickt selbst nichts und braucht deshalb **kein Geheimnis** für den Bot.
Die Registrierung ist **kostenlos**.

1. **Bot registrieren** im [Teams Developer Portal](https://dev.teams.microsoft.com) → *Tools* →
   *Bot management* → *New Bot*, Name z. B. „Aufgaben“.
   * *Configure* → **Endpoint address**: `https://<domain>/api/teams/bot`
   * *Channels*: **Microsoft Teams** muss aktiviert sein.
   * Die **Bot-ID** (eine GUID) kopieren. Ein Client-Secret wird nicht gebraucht.

   Alternativ geht eine **Azure Bot**-Ressource (Tarif F0, kostenlos; Typ „Single Tenant“) mit derselben
   Endpunkt-Adresse unter *Konfiguration* → *Messaging-Endpunkt* und aktiviertem Kanal *Microsoft Teams*.
2. **`.env`** ergänzen und die App neu starten (`docker compose up -d`):

   ```ini
   Aufgaben__Teams__BotId=<Bot-ID>
   ```

3. **Teams-Paket aktualisieren**: Verwaltung → System → „Teams-Paket herunterladen“ (enthält jetzt die Aktion)
   und im Teams Admin Center bei der vorhandenen App als neue Version hochladen. Die Version im Paket ist die
   Version des Servers. Nach einem Update ist sie also immer höher als die installierte.
4. Teams einmal komplett schließen und neu öffnen. Danach steht „Als Aufgabe“ an jeder Nachricht.

**Erreichbarkeit:** Microsoft ruft den Endpunkt aus dem Internet auf. Die Domain muss also öffentlich über
HTTPS erreichbar sein. Mit der Sperre auf Firmen-IPs in Nginx (`$aufgaben_firma`, siehe README) oder in der
Firewall kommt Teams nicht durch, dann fehlt „Als Aufgabe“ bzw. zeigt Teams einen Fehler.

## Sicherheit

* Der Endpunkt nimmt nur Aufrufe mit einem gültigen Token des Bot Framework an: Aussteller
  `https://api.botframework.com`, Zielgruppe = die eigene Bot-ID, Signaturschlüssel aus dem Verzeichnis von
  Microsoft und dort **für Teams freigegeben**, `serviceUrl` wie im Token, Mandant = der eigene Entra-Mandant.
* Die Person, die „Als Aufgabe“ wählt, muss in der App aktiv sein.
* Der Text der Nachricht liegt nur im Arbeitsspeicher der App, höchstens 30 Minuten, und nur die Person, die die
  Nachricht gewählt hat, kann ihn abrufen. Er steht nicht in der Adresse des Fensters und damit in keinem Protokoll.
* Mit KI geht der Text, wie bei „Mit KI erfassen“, an die Mistral-API (siehe Datenschutzhinweise).

## Fehlersuche

| Was passiert | Ursache |
|---|---|
| „Als Aufgabe“ fehlt im Menü | Teams-Paket nicht aktualisiert oder Teams noch nicht neu gestartet; `Aufgaben__Teams__BotId` beim Herunterladen des Pakets leer |
| Teams meldet einen Fehler beim Öffnen | Endpunkt-Adresse im Developer Portal falsch, Domain von außen nicht erreichbar oder Bot-ID in der `.env` passt nicht (im Log: „Bearer was not authenticated“) |
| „Dein Konto ist für Aufgaben nicht freigeschaltet“ | Die Person hat die App noch nie geöffnet und ist nicht aus Entra übernommen, oder sie ist deaktiviert |
| „Der Entwurf aus Teams ist abgelaufen“ | Das Fenster war länger als 30 Minuten offen oder die App wurde neu gestartet. „Als Aufgabe“ erneut wählen |
