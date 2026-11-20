const WEEKDAY = ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"];
const WEEKDAY_LONG = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"];
const MONTH = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

export function parseIso(date: string): Date {
  const [y, m, d] = date.split("-").map(Number);
  return new Date(y, m - 1, d);
}

export function daysBetween(from: string, to: string): number {
  return Math.round((parseIso(to).getTime() - parseIso(from).getTime()) / 86_400_000);
}

/** "heute", "morgen", "gestern", "Mo 23.11." */
export function relativeDay(date: string, today: string): string {
  const diff = daysBetween(today, date);
  if (diff === 0) return "heute";
  if (diff === 1) return "morgen";
  if (diff === -1) return "gestern";
  const d = parseIso(date);
  const base = `${WEEKDAY[d.getDay()]} ${d.getDate()}.${d.getMonth() + 1}.`;
  return d.getFullYear() === parseIso(today).getFullYear() ? base : `${base}${d.getFullYear()}`;
}

export function longDate(date: string): string {
  const d = parseIso(date);
  return `${WEEKDAY_LONG[d.getDay()]}, ${d.getDate()}. ${MONTH[d.getMonth()]}`;
}

export function shortDateTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("de-DE", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
}

export type DueGroup = "ueberfaellig" | "heute" | "morgen" | "woche" | "spaeter" | "ohne" | "erledigt";

export const GROUP_LABEL: Record<DueGroup, string> = {
  ueberfaellig: "Überfällig",
  heute: "Heute",
  morgen: "Morgen",
  woche: "Diese und nächste Woche",
  spaeter: "Später",
  ohne: "Ohne Termin",
  erledigt: "Erledigt",
};

export function dueGroup(dueDate: string | null, done: boolean, overdue: boolean, today: string): DueGroup {
  if (done) return "erledigt";
  if (!dueDate) return "ohne";
  if (overdue && dueDate < today) return "ueberfaellig";
  const diff = daysBetween(today, dueDate);
  if (diff < 0) return "ueberfaellig";
  if (diff === 0) return "heute";
  if (diff === 1) return "morgen";
  if (diff <= 13) return "woche";
  return "spaeter";
}

/** Für <input type="datetime-local">: ISO-Zeitpunkt → "2026-11-23T09:00" in lokaler Zeit. */
export function toLocalInput(iso: string | null): string {
  if (!iso) return "";
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function fromLocalInput(value: string): string | null {
  if (!value) return null;
  return new Date(value).toISOString();
}

export const RECURRENCE_LABEL: Record<string, string> = {
  "": "Keine",
  taeglich: "Täglich",
  werktags: "Werktags",
  woechentlich: "Wöchentlich",
  monatlich: "Monatlich",
  jaehrlich: "Jährlich",
};
