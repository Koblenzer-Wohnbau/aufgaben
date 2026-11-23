import { describe, expect, it } from "vitest";
import { addDays, defaultFollowUp, followUpChoices, followUpDue, laterAction, laterChoices, sinceLabel, weekdayDate, workday } from "./later";

// 2026-11-20 ist ein Freitag, 2026-11-23 ein Montag.
describe("Später und Warte auf …", () => {
  it("rechnet Tage über Monatsgrenzen und schiebt Wochenenden auf Montag", () => {
    expect(addDays("2026-11-30", 1)).toBe("2026-12-01");
    expect(workday("2026-11-21")).toBe("2026-11-23");
    expect(workday("2026-11-22")).toBe("2026-11-23");
    expect(workday("2026-11-24")).toBe("2026-11-24");
    expect(weekdayDate("2026-11-24")).toBe("Di 24.11.");
  });

  it("bietet freitags Montag statt „Morgen“ an, und jeden Tag nur einmal", () => {
    expect(laterChoices("2026-11-20")).toEqual([
      { label: "Montag", date: "2026-11-23" },
      { label: "In einer Woche", date: "2026-11-27" },
      { label: "In zwei Wochen", date: "2026-12-04" },
    ]);
    expect(laterChoices("2026-11-24").map((c) => c.label)).toEqual(["Morgen", "Nächste Woche", "In einer Woche", "In zwei Wochen"]);
    expect(laterChoices("2026-11-24")[1].date).toBe("2026-11-30");
  });

  it("schlägt zum Nachhaken Werktage vor, Vorgabe in einer Woche", () => {
    expect(followUpChoices("2026-11-19").map((c) => c.date)).toEqual(["2026-11-23", "2026-11-26", "2026-12-03"]);
    expect(defaultFollowUp("2026-11-21")).toBe("2026-11-30");
  });

  it("beschreibt, seit wann gewartet wird", () => {
    expect(sinceLabel("2026-11-24", "2026-11-24")).toBe("seit heute");
    expect(sinceLabel("2026-11-23", "2026-11-24")).toBe("seit gestern");
    expect(sinceLabel("2026-11-14", "2026-11-24")).toBe("seit 10 Tagen");
    expect(sinceLabel(null, "2026-11-24")).toBe("");
  });

  it("erkennt, wann nachgehakt werden muss", () => {
    const task = { waitingFor: "Fa. Müller", snoozedUntil: "2026-11-24", status: "offen" as const };
    expect(followUpDue(task, "2026-11-23")).toBe(false);
    expect(followUpDue(task, "2026-11-24")).toBe(true);
    expect(followUpDue({ ...task, status: "erledigt" }, "2026-11-25")).toBe(false);
    expect(followUpDue({ ...task, waitingFor: "" }, "2026-11-25")).toBe(false);
  });

  it("stellt zurück, verschiebt aber bei schon fälligen Aufgaben die Fälligkeit", () => {
    const today = "2026-11-24";
    expect(laterAction({ kind: "aufgabe", dueDate: null, waitingFor: "" }, "2026-11-30", today)).toEqual({
      patch: { snoozedUntil: "2026-11-30" },
      message: "Zurückgestellt bis Mo 30.11.",
    });
    expect(laterAction({ kind: "aufgabe", dueDate: "2026-11-23", waitingFor: "" }, "2026-11-30", today).patch).toEqual({ dueDate: "2026-11-30", myDay: false });
    expect(laterAction({ kind: "wiedervorlage", dueDate: today, waitingFor: "" }, "2026-11-30", today).patch).toEqual({ snoozedUntil: "2026-11-30" });
    expect(laterAction({ kind: "aufgabe", dueDate: today, waitingFor: "Fa. Müller" }, "2026-11-30", today).message).toBe("Nachhaken am Mo 30.11.");
    expect(laterAction({ kind: "aufgabe", dueDate: "2026-11-26", waitingFor: "" }, "2026-11-30", today).message).toMatch(/schon am Do 26\.11\./);
  });
});
