# Sicherheit

## Sicherheitslücken melden

Bitte **keine öffentlichen Issues** für Sicherheitslücken anlegen. Stattdessen über
GitHub → *Security* → *Report a vulnerability* (private Meldung) melden.

Wir bestätigen den Eingang in der Regel innerhalb weniger Werktage.

## Grundsätze

* Keine Geheimnisse im Repository: Konfiguration nur über `.env` und `secrets/` auf dem Server.
* Jeder Push wird mit gitleaks auf versehentlich eingecheckte Geheimnisse geprüft.
* Abhängigkeiten sind auf feste Versionen gesetzt und werden über Dependabot aktualisiert.
* Private Aufgaben sind auch für Administratoren nicht sichtbar; Änderungen an dieser Regel
  brauchen einen Test in `tests/Aufgaben.Tests/ApiTests.cs`.
