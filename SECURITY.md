# Sicherheit

## Sicherheitslücken melden

Bitte **keine öffentlichen Issues** für Sicherheitslücken anlegen. Stattdessen über
GitHub → *Security* → *Report a vulnerability* (private Meldung) melden.

Wir bestätigen den Eingang in der Regel innerhalb weniger Werktage.

## Grundsätze

* Keine Geheimnisse im Repository: Konfiguration nur über `.env` und `secrets/` auf dem Server.
* Jeder Push auf `main` und jeder Pull Request wird mit gitleaks auf versehentlich eingecheckte
  Geheimnisse geprüft.
* Die genauen Versionen der Abhängigkeiten stehen in `package-lock.json` bzw. den Projektdateien;
  Dependabot schlägt Aktualisierungen vor.
* Private Aufgaben sind auch für Administratoren nicht sichtbar; Änderungen an dieser Regel
  brauchen einen Test in `tests/Aufgaben.Tests/ApiTests.cs`.
