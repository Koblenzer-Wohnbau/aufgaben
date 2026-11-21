import { daysBetween, parseIso, relativeDay } from "./dates";
import { EVENT_TEXT } from "./eventText";
import { addDays, weekdayDate, workday } from "./later";
import type { AbsenceItem, HandoverState, SummaryEvent } from "./types";

/** Urlaubsübergabe: Tage, Texte und Gruppen für Planung, Banner und „Was ist passiert?“. */

/** Erster Arbeitstag nach dem letzten Urlaubstag (wie auf dem Server). */
export function returnDay(until: string): string {
  return workday(addDays(until, 1));
}

/** Vorschlag für einen neuen Urlaub: ab dem nächsten Werktag eine Arbeitswoche lang. */
export function suggestedAbsence(today: string): { from: string; until: string } {
  const from = workday(addDays(today, 1));
  const daysToFriday = (5 - parseIso(from).getDay() + 7) % 7;
  return { from, until: addDays(from, daysToFriday) };
}

/** "Mo 24.11. – Fr 5.12." */
export function rangeLabel(from: string, until: string): string {
  return from === until ? weekdayDate(from) : `${weekdayDate(from)} – ${weekdayDate(until)}`;
}

/** Für das Banner der Vertretung: "heute letzter Tag", "noch bis morgen", "noch 4 Tage" (heute mitgezählt). */
export function remainingLabel(until: string, today: string): string {
  const days = daysBetween(today, until);
  if (days <= 0) return "heute letzter Tag";
  if (days === 1) return "noch bis morgen";
  return `noch ${days + 1} Tage`;
}

/** Prüfung vor dem Absenden – dieselben Regeln wie auf dem Server, damit Fehler gleich am Feld stehen. */
export function absenceProblem(from: string, until: string, deputyId: number | null, meId: number, today: string): string | null {
  if (!from || !until) return "Bitte den ersten und den letzten Urlaubstag angeben.";
  if (until < from) return "Der letzte Urlaubstag liegt vor dem ersten.";
  if (until < today) return "Dieser Urlaub ist schon vorbei.";
  if (deputyId === null) return "Bitte eine Vertretung auswählen.";
  if (deputyId === meId) return "Die Vertretung muss jemand anderes sein.";
  return null;
}

/** Warum eine Aufgabe in der Übergabe steht – danger: überfällig oder Nachhaken ist schon dran. */
export function reasonLabel(item: Pick<AbsenceItem, "kind" | "dueDate" | "waitingFor" | "followUp">, from: string, today: string): { text: string; danger: boolean } {
  if (item.waitingFor) {
    if (!item.followUp) return { text: `Warte auf ${item.waitingFor}`, danger: false };
    const due = item.followUp <= today;
    return { text: `Warte auf ${item.waitingFor} · nachhaken ${due ? `seit ${weekdayDate(item.followUp)}` : relativeDay(item.followUp, today)}`, danger: due };
  }
  if (!item.dueDate) return { text: "", danger: false };
  if (item.dueDate < today) return { text: `überfällig seit ${weekdayDate(item.dueDate)}`, danger: true };
  const what = item.kind === "wiedervorlage" ? "Wiedervorlage" : "fällig";
  return { text: `${what} ${relativeDay(item.dueDate, today)}${item.dueDate < from ? " – noch vor dem Urlaub" : ""}`, danger: false };
}

/** Was nach der Entscheidung mit der Aufgabe passiert. */
export function decisionLabel(item: AbsenceItem, deputyId: number, back: string, today: string): string {
  if (item.decision === "uebergeben") {
    return item.assignee.id === deputyId ? `An ${item.assignee.name} übergeben` : `Liegt jetzt bei ${item.assignee.name}`;
  }
  if (item.decision !== "behalten") return "";
  // Eine Frist geht immer vor (wie bei „Später“): Was schon fällig ist, bleibt sichtbar.
  if (!item.waitingFor && item.kind !== "wiedervorlage" && item.dueDate && item.dueDate <= today) return "Bleibt bei dir – die Frist ist schon da";
  if (!item.waitingFor && item.kind !== "wiedervorlage" && item.dueDate && item.dueDate < back) {
    return `Bleibt bei dir – ausgeblendet bis zur Fälligkeit ${weekdayDate(item.dueDate)}`;
  }
  return `Bleibt bei dir – wieder da am ${weekdayDate(back)}`;
}

export interface SummaryGroups {
  /** noch bei der Vertretung – „Zurück an mich“ */
  open: HandoverState[];
  done: HandoverState[];
  /** schon wieder bei mir */
  back: HandoverState[];
  /** von der Vertretung an jemand anderen weitergegeben */
  elsewhere: HandoverState[];
}

export function summaryGroups(handovers: HandoverState[], meId: number): SummaryGroups {
  const groups: SummaryGroups = { open: [], done: [], back: [], elsewhere: [] };
  for (const h of handovers) {
    if (h.status === "erledigt") groups.done.push(h);
    else if (h.canTakeBack) groups.open.push(h);
    else if (h.assignee.id === meId) groups.back.push(h);
    else groups.elsewhere.push(h);
  }
  return groups;
}

/** Ereignisse je Aufgabe, die Aufgabe mit dem neuesten Ereignis zuerst. */
export function eventsByTask(events: SummaryEvent[]): { taskId: number; title: string; events: SummaryEvent[] }[] {
  const map = new Map<number, { taskId: number; title: string; events: SummaryEvent[] }>();
  for (const e of [...events].sort((a, b) => b.createdAt.localeCompare(a.createdAt))) {
    if (!map.has(e.taskId)) map.set(e.taskId, { taskId: e.taskId, title: e.taskTitle, events: [] });
    map.get(e.taskId)!.events.push(e);
  }
  return [...map.values()];
}

/** "hat kommentiert: „Schlüssel sind beim Hausmeister“", "hat erledigt" … */
export function summaryLine(e: Pick<SummaryEvent, "kind" | "text">): string {
  if (e.kind === "kommentar") return `hat kommentiert: „${e.text.length > 160 ? `${e.text.slice(0, 159)}…` : e.text}“`;
  if (e.kind === "angelegt") return e.text ? `hat die Aufgabe ${e.text} angelegt` : "hat die Aufgabe angelegt";
  return `${EVENT_TEXT[e.kind] ?? e.kind} ${e.text}`.trim();
}

/** "1 Aufgabe", "3 Aufgaben" */
export function taskCount(n: number): string {
  return n === 1 ? "1 Aufgabe" : `${n} Aufgaben`;
}
