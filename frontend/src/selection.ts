import type { BulkResult } from "./types";

/** Auswahl mehrerer Aufgaben und Papierkorb: reine Hilfsfunktionen, damit sie testbar bleiben. */

/** Tage, die gelöschte Aufgaben im Papierkorb bleiben, muss zu TaskService.TrashDays passen. */
export const TRASH_DAYS = 30;

/**
 * Klick auf eine Aufgabe im Auswahlmodus. Mit Umschalt wird alles zwischen dem letzten Klick (anchor)
 * und dieser Aufgabe in der angezeigten Reihenfolge ausgewählt, sonst nur diese eine umgeschaltet.
 */
export function toggleSelection(selected: ReadonlySet<number>, order: readonly number[], id: number, anchor: number | null, range: boolean): Set<number> {
  const next = new Set(selected);
  const from = anchor === null ? -1 : order.indexOf(anchor);
  const to = order.indexOf(id);
  if (range && from >= 0 && to >= 0) {
    const [a, b] = from < to ? [from, to] : [to, from];
    for (const x of order.slice(a, b + 1)) next.add(x);
    return next;
  }
  if (next.has(id)) next.delete(id);
  else next.add(id);
  return next;
}

/** Zustand des Kästchens „Alle auswählen“. */
export function allState(selected: ReadonlySet<number>, ids: readonly number[]): boolean | "mixed" {
  if (ids.length === 0) return false;
  const count = ids.filter((id) => selected.has(id)).length;
  if (count === 0) return false;
  return count === ids.length ? true : "mixed";
}

/** Wie lange eine Aufgabe noch im Papierkorb bleibt, in ganzen Tagen (mindestens 0). */
export function trashDaysLeft(deletedAt: string, now: Date = new Date()): number {
  const end = new Date(deletedAt).getTime() + TRASH_DAYS * 86_400_000;
  return Math.max(0, Math.ceil((end - now.getTime()) / 86_400_000));
}

/** „1 Aufgabe“, „3 Aufgaben“ */
export function tasksLabel(n: number): string {
  return n === 1 ? "1 Aufgabe" : `${n} Aufgaben`;
}

/** Meldung nach einer Aktion für mehrere Aufgaben. */
export function bulkMessage(action: "erledigen" | "loeschen" | "wiederherstellen" | "endgueltig-loeschen", r: BulkResult): string {
  const what = {
    erledigen: "erledigt",
    loeschen: "in den Papierkorb gelegt",
    wiederherstellen: "wiederhergestellt",
    "endgueltig-loeschen": "endgültig gelöscht",
  }[action];
  const head = `${tasksLabel(r.done)} ${what}`;
  if (r.skipped === 0) return head;
  const why = action === "loeschen" ? "nicht von dir angelegt" : "nicht erlaubt oder nicht mehr vorhanden";
  return `${head}, ${r.skipped} übersprungen (${why})`;
}
