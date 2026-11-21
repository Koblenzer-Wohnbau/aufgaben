import { describe, expect, it } from "vitest";
import { allState, bulkMessage, toggleSelection, trashDaysLeft } from "./selection";

describe("Auswahl und Papierkorb", () => {
  const order = [10, 11, 12, 13, 14];

  it("schaltet einzelne Aufgaben ein und aus", () => {
    const one = toggleSelection(new Set(), order, 12, null, false);
    expect([...one]).toEqual([12]);
    expect([...toggleSelection(one, order, 12, 12, false)]).toEqual([]);
  });

  it("wählt mit Umschalt den Bereich seit dem letzten Klick aus, in beide Richtungen", () => {
    expect([...toggleSelection(new Set([11]), order, 13, 11, true)].sort()).toEqual([11, 12, 13]);
    expect([...toggleSelection(new Set(), order, 10, 12, true)].sort()).toEqual([10, 11, 12]);
    // Ohne vorherigen Klick wirkt Umschalt wie ein normaler Klick
    expect([...toggleSelection(new Set(), order, 14, null, true)]).toEqual([14]);
  });

  it("zeigt „Alle auswählen“ als an, aus oder teilweise", () => {
    expect(allState(new Set(), order)).toBe(false);
    expect(allState(new Set([10, 11]), order)).toBe("mixed");
    expect(allState(new Set(order), order)).toBe(true);
    expect(allState(new Set([99]), [])).toBe(false);
  });

  it("rechnet die verbleibenden Tage im Papierkorb", () => {
    const now = new Date("2026-11-21T12:00:00Z");
    expect(trashDaysLeft("2026-11-21T11:00:00Z", now)).toBe(30);
    expect(trashDaysLeft("2026-10-23T12:00:00Z", now)).toBe(1);
    expect(trashDaysLeft("2026-09-01T00:00:00Z", now)).toBe(0);
  });

  it("meldet Ergebnis und Übersprungenes verständlich", () => {
    expect(bulkMessage("loeschen", { done: 1, skipped: 0 })).toBe("1 Aufgabe in den Papierkorb gelegt");
    expect(bulkMessage("loeschen", { done: 3, skipped: 2 })).toBe("3 Aufgaben in den Papierkorb gelegt, 2 übersprungen (nicht von dir angelegt)");
    expect(bulkMessage("erledigen", { done: 4, skipped: 0 })).toBe("4 Aufgaben erledigt");
  });
});
