import { describe, expect, it } from "vitest";
import { activeMention, parseQuickAdd } from "./quickadd";

// Freitag, 20. November 2026, 09:00
const today = new Date(2026, 10, 20, 9, 0);

describe("parseQuickAdd", () => {
  it("erkennt das Beispiel aus der Präsentation vollständig", () => {
    const r = parseQuickAdd("Montag 10 Uhr Angebot Dachdecker prüfen #1001 @Becker !hoch", today);
    expect(r.title).toBe("Angebot Dachdecker prüfen");
    expect(r.dueDate).toBe("2026-11-23");
    expect(r.dueTime).toBe("10:00");
    expect(r.projectQuery).toBe("1001");
    expect(r.personQuery).toBe("Becker");
    expect(r.priority).toBe("hoch");
  });

  it("versteht heute, morgen und übermorgen", () => {
    expect(parseQuickAdd("Rechnung prüfen heute", today).dueDate).toBe("2026-11-20");
    expect(parseQuickAdd("morgen Fenster bestellen", today).dueDate).toBe("2026-11-21");
    const r = parseQuickAdd("übermorgen Termin Gutachter", today);
    expect(r.dueDate).toBe("2026-11-22");
    expect(r.title).toBe("Termin Gutachter");
  });

  it("liest deutsche Datumsangaben mit und ohne Jahr", () => {
    expect(parseQuickAdd("Abnahme am 3.12.", today).dueDate).toBe("2026-12-03");
    expect(parseQuickAdd("Frist 15.01.2027", today).dueDate).toBe("2027-01-15");
    expect(parseQuickAdd("Frist 15.01.27", today).dueDate).toBe("2027-01-15");
    // Datum schon vorbei → nächstes Jahr
    expect(parseQuickAdd("Jahresgespräch 10.2.", today).dueDate).toBe("2027-02-10");
  });

  it("verwechselt ein Datum nicht mit einer Uhrzeit", () => {
    const r = parseQuickAdd("Begehung 10.12. um 14:30", today);
    expect(r.dueDate).toBe("2026-12-10");
    expect(r.dueTime).toBe("14:30");
    expect(r.title).toBe("Begehung");
  });

  it("nimmt bei reiner Uhrzeit heute oder morgen", () => {
    expect(parseQuickAdd("Anruf 15 Uhr", today).dueDate).toBe("2026-11-20");
    expect(parseQuickAdd("Anruf 8 Uhr", today).dueDate).toBe("2026-11-21");
  });

  it("erkennt relative Angaben", () => {
    expect(parseQuickAdd("Nachfassen in 3 Tagen", today).dueDate).toBe("2026-11-23");
    expect(parseQuickAdd("Nachfassen in 2 Wochen", today).dueDate).toBe("2026-12-04");
    expect(parseQuickAdd("Bericht nächste Woche", today).dueDate).toBe("2026-11-23");
    expect(parseQuickAdd("Bericht Ende der Woche", today).dueDate).toBe("2026-11-20");
    expect(parseQuickAdd("Abrechnung Monatsende", today).dueDate).toBe("2026-11-30");
  });

  it("erkennt Wiederholungen", () => {
    const r = parseQuickAdd("Baustellenrundgang jeden Montag 8 Uhr", today);
    expect(r.recurrence).toBe("woechentlich");
    expect(r.dueDate).toBe("2026-11-23");
    expect(r.dueTime).toBe("08:00");
    expect(r.title).toBe("Baustellenrundgang");
    expect(parseQuickAdd("Postfach prüfen werktags", today).recurrence).toBe("werktags");
    expect(parseQuickAdd("Zählerstände monatlich", today).recurrence).toBe("monatlich");
  });

  it("erkennt Wiedervorlagen", () => {
    const r = parseQuickAdd("WV Gewährleistung Dach in 2 Wochen", today);
    expect(r.kind).toBe("wiedervorlage");
    expect(r.title).toBe("Gewährleistung Dach");
  });

  it("lässt normale Wörter mit Umlauten in Ruhe", () => {
    const r = parseQuickAdd("Morgenrunde Übergabe Heutiger Stand", today);
    expect(r.dueDate).toBeUndefined();
    expect(r.title).toBe("Morgenrunde Übergabe Heutiger Stand");
  });

  it("behält Projektnummern im Titel nicht doppelt", () => {
    const r = parseQuickAdd("Schlussrechnung #1001 freigeben", today);
    expect(r.title).toBe("Schlussrechnung freigeben");
    expect(r.projectQuery).toBe("1001");
  });

  it("fällt auf die Eingabe zurück, wenn kein Titel übrig bleibt", () => {
    expect(parseQuickAdd("morgen", today).title).toBe("morgen");
  });
});

describe("activeMention", () => {
  it("findet das aktuell getippte @-Wort", () => {
    expect(activeMention("Angebot @Bec", 12)).toEqual({ kind: "@", query: "Bec", start: 8 });
  });
  it("findet das aktuell getippte #-Wort", () => {
    expect(activeMention("#61", 3)).toEqual({ kind: "#", query: "61", start: 0 });
  });
  it("ignoriert E-Mail-Adressen mitten im Wort", () => {
    expect(activeMention("mail@firma", 10)).toBeNull();
  });
});
