import { daysBetween, parseIso } from "./dates";
import type { Task } from "./types";

/** „Später“ und „Warte auf …“: Tage auswählen, Texte bilden, das Richtige an den Server schicken. */

const WEEKDAY = ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"];

export interface DateChoice {
  label: string;
  date: string;
}

/** Datum in lokaler Zeit → "2026-11-24" */
export function isoDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function addDays(date: string, days: number): string {
  const d = parseIso(date);
  d.setDate(d.getDate() + days);
  return isoDate(d);
}

/** Samstag und Sonntag → Montag (wie auf dem Server). */
export function workday(date: string): string {
  const day = parseIso(date).getDay();
  if (day === 6) return addDays(date, 2);
  if (day === 0) return addDays(date, 1);
  return date;
}

/** "Mo 24.11." */
export function weekdayDate(date: string): string {
  const d = parseIso(date);
  return `${WEEKDAY[d.getDay()]} ${d.getDate()}.${d.getMonth() + 1}.`;
}

/** Gleiche Tage nur einmal anbieten (z. B. freitags: „Montag“ statt „Morgen“ und „Nächste Woche“). */
function unique(choices: DateChoice[]): DateChoice[] {
  const seen = new Set<string>();
  return choices.filter((c) => {
    if (seen.has(c.date)) return false;
    seen.add(c.date);
    return true;
  });
}

/** Zurückstellen: morgen, nächste Woche (Montag), in einer oder zwei Wochen – immer an einem Werktag. */
export function laterChoices(today: string): DateChoice[] {
  const tomorrow = addDays(today, 1);
  const next = workday(tomorrow);
  const daysToMonday = (8 - parseIso(today).getDay()) % 7 || 7;
  return unique([
    { label: next === tomorrow ? "Morgen" : "Montag", date: next },
    { label: "Nächste Woche", date: addDays(today, daysToMonday) },
    { label: "In einer Woche", date: workday(addDays(today, 7)) },
    { label: "In zwei Wochen", date: workday(addDays(today, 14)) },
  ]);
}

/** Nachhaken: in drei Tagen, in einer Woche (Vorgabe, wie auf dem Server) oder in zwei Wochen. */
export function followUpChoices(today: string): DateChoice[] {
  return unique([
    { label: "In 3 Tagen", date: workday(addDays(today, 3)) },
    { label: "In einer Woche", date: workday(addDays(today, 7)) },
    { label: "In zwei Wochen", date: workday(addDays(today, 14)) },
  ]);
}

export function defaultFollowUp(today: string): string {
  return workday(addDays(today, 7));
}

/** "seit heute", "seit gestern", "seit 5 Tagen" */
export function sinceLabel(since: string | null, today: string): string {
  if (!since) return "";
  const days = daysBetween(since, today);
  if (days <= 0) return "seit heute";
  if (days === 1) return "seit gestern";
  return `seit ${days} Tagen`;
}

/** Bei „Warte auf …“ ist der Tag zum Nachhaken da. */
export function followUpDue(task: Pick<Task, "waitingFor" | "snoozedUntil" | "status">, today: string): boolean {
  return task.status !== "erledigt" && !!task.waitingFor && !!task.snoozedUntil && task.snoozedUntil <= today;
}

/**
 * Was „Später“ bei dieser Aufgabe bedeutet:
 * - „Warte auf …“: später nachhaken,
 * - Wiedervorlage: Wiedervorlage verschieben (der Server verschiebt die Fälligkeit mit),
 * - normale Aufgabe, die schon fällig ist: Fälligkeit verschieben (zurückstellen ginge nicht, die Frist geht vor),
 * - sonst: bis zu dem Tag zurückstellen.
 */
export function laterAction(
  task: Pick<Task, "kind" | "dueDate" | "waitingFor">,
  date: string,
  today: string,
): { patch: Record<string, unknown>; message: string } {
  const when = weekdayDate(date);
  if (task.waitingFor) return { patch: { snoozedUntil: date }, message: `Nachhaken am ${when}` };
  if (task.kind === "wiedervorlage") return { patch: { snoozedUntil: date }, message: `Wiedervorlage am ${when}` };
  if (task.dueDate && task.dueDate <= today) {
    return { patch: { dueDate: date, myDay: false }, message: `Fälligkeit auf ${when} verschoben` };
  }
  if (task.dueDate && task.dueDate < date) {
    return { patch: { snoozedUntil: date }, message: `Zurückgestellt – wegen der Fälligkeit schon am ${weekdayDate(task.dueDate)} wieder da` };
  }
  return { patch: { snoozedUntil: date }, message: `Zurückgestellt bis ${when}` };
}
