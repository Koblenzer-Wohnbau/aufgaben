import { describe, expect, it } from "vitest";
import {
  absenceProblem,
  decisionLabel,
  eventsByTask,
  rangeLabel,
  reasonLabel,
  remainingLabel,
  returnDay,
  suggestedAbsence,
  summaryGroups,
  summaryLine,
  taskCount,
} from "./absence";
import type { AbsenceItem, HandoverState, SummaryEvent, UserRef } from "./types";

// 2026-11-20 ist ein Freitag, 2026-11-23 ein Montag.
const jonas: UserRef = { id: 2, name: "Jonas Wagner", email: null, jobTitle: null };
const anna: UserRef = { id: 3, name: "Anna Schneider", email: null, jobTitle: null };
const me: UserRef = { id: 1, name: "Max Becker", email: null, jobTitle: null };

function item(over: Partial<AbsenceItem>): AbsenceItem {
  return { taskId: 1, title: "Aufgabe", kind: "aufgabe", dueDate: null, waitingFor: "", followUp: null, decision: null, assignee: me, canOpen: true, ...over };
}

function handover(over: Partial<HandoverState>): HandoverState {
  return { taskId: 1, title: "Aufgabe", status: "offen", assignee: jonas, completedBy: null, completedAt: null, canTakeBack: false, canOpen: true, ...over };
}

describe("Urlaubsübergabe", () => {
  it("ist am ersten Arbeitstag nach dem Urlaub zurück", () => {
    expect(returnDay("2026-11-27")).toBe("2026-11-30");
    expect(returnDay("2026-11-28")).toBe("2026-11-30");
    expect(returnDay("2026-11-24")).toBe("2026-11-25");
  });

  it("schlägt ab dem nächsten Werktag eine Arbeitswoche vor", () => {
    expect(suggestedAbsence("2026-11-20")).toEqual({ from: "2026-11-23", until: "2026-11-27" });
    expect(suggestedAbsence("2026-11-24")).toEqual({ from: "2026-11-25", until: "2026-11-27" });
    expect(rangeLabel("2026-11-23", "2026-12-04")).toBe("Mo 23.11. – Fr 4.12.");
    expect(rangeLabel("2026-11-23", "2026-11-23")).toBe("Mo 23.11.");
  });

  it("zählt im Banner der Vertretung heute mit", () => {
    expect(remainingLabel("2026-11-27", "2026-11-24")).toBe("noch 4 Tage");
    expect(remainingLabel("2026-11-25", "2026-11-24")).toBe("noch bis morgen");
    expect(remainingLabel("2026-11-24", "2026-11-24")).toBe("heute letzter Tag");
  });

  it("prüft den Urlaub wie der Server", () => {
    const today = "2026-11-24";
    expect(absenceProblem("2026-11-30", "2026-11-27", 2, 1, today)).toBe("Der letzte Urlaubstag liegt vor dem ersten.");
    expect(absenceProblem("2026-11-10", "2026-11-20", 2, 1, today)).toBe("Dieser Urlaub ist schon vorbei.");
    expect(absenceProblem("2026-11-25", "2026-11-27", null, 1, today)).toBe("Bitte eine Vertretung auswählen.");
    expect(absenceProblem("2026-11-25", "2026-11-27", 1, 1, today)).toBe("Die Vertretung muss jemand anderes sein.");
    expect(absenceProblem("", "2026-11-27", 2, 1, today)).not.toBeNull();
    expect(absenceProblem("2026-11-24", "2026-11-24", 2, 1, today)).toBeNull();
  });

  it("sagt, warum eine Aufgabe in der Übergabe steht", () => {
    const today = "2026-11-24";
    const from = "2026-11-26";
    expect(reasonLabel(item({ dueDate: "2026-11-20" }), from, today)).toEqual({ text: "überfällig seit Fr 20.11.", danger: true });
    expect(reasonLabel(item({ dueDate: "2026-11-25" }), from, today).text).toBe("fällig morgen – noch vor dem Urlaub");
    expect(reasonLabel(item({ dueDate: "2026-11-30", kind: "wiedervorlage" }), from, today).text).toBe("Wiedervorlage Mo 30.11.");
    expect(reasonLabel(item({ waitingFor: "Fa. Müller", followUp: "2026-11-23" }), from, today)).toEqual({
      text: "Warte auf Fa. Müller · nachhaken seit Mo 23.11.",
      danger: true,
    });
    expect(reasonLabel(item({ waitingFor: "Fa. Müller", followUp: "2026-11-27" }), from, today).text).toBe("Warte auf Fa. Müller · nachhaken Fr 27.11.");
  });

  it("beschreibt, was nach der Entscheidung passiert", () => {
    const today = "2026-11-24";
    const back = "2026-12-07";
    expect(decisionLabel(item({ decision: "uebergeben", assignee: jonas }), jonas.id, back, today)).toBe("An Jonas Wagner übergeben");
    expect(decisionLabel(item({ decision: "uebergeben", assignee: anna }), jonas.id, back, today)).toBe("Liegt jetzt bei Anna Schneider");
    expect(decisionLabel(item({ decision: "behalten" }), jonas.id, back, today)).toBe("Bleibt bei dir – wieder da am Mo 7.12.");
    expect(decisionLabel(item({ decision: "behalten", dueDate: "2026-11-20" }), jonas.id, back, today)).toBe("Bleibt bei dir – die Frist ist schon da");
    expect(decisionLabel(item({ decision: "behalten", dueDate: "2026-12-02" }), jonas.id, back, today)).toBe(
      "Bleibt bei dir – ausgeblendet bis zur Fälligkeit Mi 2.12.",
    );
    // Wiedervorlage und Nachhaken wandern mit
    expect(decisionLabel(item({ decision: "behalten", kind: "wiedervorlage", dueDate: "2026-12-02" }), jonas.id, back, today)).toBe(
      "Bleibt bei dir – wieder da am Mo 7.12.",
    );
    expect(decisionLabel(item({}), jonas.id, back, today)).toBe("");
  });

  it("sortiert übergebene Aufgaben nach der Rückkehr", () => {
    const groups = summaryGroups(
      [
        handover({ taskId: 1, status: "erledigt", completedBy: jonas }),
        handover({ taskId: 2, canTakeBack: true }),
        handover({ taskId: 3, assignee: me }),
        handover({ taskId: 4, assignee: anna }),
      ],
      me.id,
    );
    expect(groups.done.map((h) => h.taskId)).toEqual([1]);
    expect(groups.open.map((h) => h.taskId)).toEqual([2]);
    expect(groups.back.map((h) => h.taskId)).toEqual([3]);
    expect(groups.elsewhere.map((h) => h.taskId)).toEqual([4]);
  });

  it("fasst Neues je Aufgabe zusammen, Neuestes zuerst", () => {
    const e = (id: number, taskId: number, createdAt: string, kind = "kommentar", text = "ok"): SummaryEvent => ({
      id,
      taskId,
      taskTitle: `Aufgabe ${taskId}`,
      kind,
      text,
      actor: anna,
      createdAt,
    });
    const grouped = eventsByTask([e(1, 10, "2026-11-25T09:00:00Z"), e(2, 20, "2026-11-26T09:00:00Z"), e(3, 10, "2026-11-27T09:00:00Z")]);
    expect(grouped.map((g) => g.taskId)).toEqual([10, 20]);
    expect(grouped[0].events.map((x) => x.id)).toEqual([3, 1]);

    expect(summaryLine({ kind: "kommentar", text: "Schlüssel liegen beim Hausmeister" })).toBe("hat kommentiert: „Schlüssel liegen beim Hausmeister“");
    expect(summaryLine({ kind: "erledigt", text: "" })).toBe("hat erledigt");
    expect(summaryLine({ kind: "zugewiesen", text: "an Max Becker" })).toBe("hat zugewiesen an Max Becker");
    expect(summaryLine({ kind: "angelegt", text: "aus einer E-Mail" })).toBe("hat die Aufgabe aus einer E-Mail angelegt");
    expect(taskCount(1)).toBe("1 Aufgabe");
    expect(taskCount(3)).toBe("3 Aufgaben");
  });
});
