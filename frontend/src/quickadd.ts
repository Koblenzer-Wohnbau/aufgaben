/**
 * Schnelleingabe: erkennt in einer deutschen Eingabezeile Termin, Uhrzeit, Projekt (#),
 * Person (@), Priorität (!), Wiedervorlage und Wiederholung.
 *
 *   "Montag 10 Uhr Angebot Dachdecker prüfen #1001 @Becker !hoch"
 *   → Titel "Angebot Dachdecker prüfen", Mo, 10:00, Projekt "1001", Person "Becker", hoch
 *
 * Läuft komplett im Browser, keine KI, keine Daten nach außen.
 */

export type Recurrence = "taeglich" | "werktags" | "woechentlich" | "monatlich" | "jaehrlich";

export type TokenType = "date" | "time" | "project" | "person" | "priority" | "kind" | "recurrence";

export interface QuickToken {
  type: TokenType;
  text: string;
}

export interface ParsedQuickAdd {
  title: string;
  dueDate?: string;
  dueTime?: string;
  priority?: "hoch" | "niedrig";
  kind?: "wiedervorlage";
  recurrence?: Recurrence;
  projectQuery?: string;
  personQuery?: string;
  tokens: QuickToken[];
}

const WEEKDAYS: Record<string, number> = {
  sonntag: 0, montag: 1, dienstag: 2, mittwoch: 3, donnerstag: 4, freitag: 5, samstag: 6,
  so: 0, mo: 1, di: 2, mi: 3, do: 4, fr: 5, sa: 6,
};

// Wortgrenzen, die auch mit Umlauten funktionieren.
const B = "(?<=^|[\\s,;(])";
const E = "(?=$|[\\s,;.!?)])";

function re(body: string): RegExp {
  return new RegExp(B + "(?:" + body + ")" + E, "iu");
}

export function toIsoDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

function addDays(d: Date, n: number): Date {
  const r = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  r.setDate(r.getDate() + n);
  return r;
}

function nextWeekday(today: Date, weekday: number, allowToday = false): Date {
  let diff = (weekday - today.getDay() + 7) % 7;
  if (diff === 0 && !allowToday) diff = 7;
  return addDays(today, diff);
}

function endOfMonth(today: Date): Date {
  return new Date(today.getFullYear(), today.getMonth() + 1, 0);
}

interface Rule {
  type: TokenType;
  pattern: RegExp;
  apply: (m: RegExpExecArray, r: ParsedQuickAdd, today: Date) => boolean;
}

const RULES: Rule[] = [
  // Wiederholung mit Wochentag: "jeden Montag"
  {
    type: "recurrence",
    pattern: re("jeden\\s+(montag|dienstag|mittwoch|donnerstag|freitag|samstag|sonntag)"),
    apply: (m, r, today) => {
      r.recurrence = "woechentlich";
      if (!r.dueDate) r.dueDate = toIsoDate(nextWeekday(today, WEEKDAYS[m[1].toLowerCase()], true));
      return true;
    },
  },
  {
    type: "recurrence",
    pattern: re("jeden\\s+tag|täglich|taeglich"),
    apply: (_m, r) => ((r.recurrence = "taeglich"), true),
  },
  {
    type: "recurrence",
    pattern: re("werktags|jeden\\s+werktag"),
    apply: (_m, r) => ((r.recurrence = "werktags"), true),
  },
  {
    type: "recurrence",
    pattern: re("jede\\s+woche|wöchentlich|woechentlich"),
    apply: (_m, r) => ((r.recurrence = "woechentlich"), true),
  },
  {
    type: "recurrence",
    pattern: re("jeden\\s+monat|monatlich"),
    apply: (_m, r) => ((r.recurrence = "monatlich"), true),
  },
  {
    type: "recurrence",
    pattern: re("jedes\\s+jahr|jährlich|jaehrlich"),
    apply: (_m, r) => ((r.recurrence = "jaehrlich"), true),
  },
  {
    type: "kind",
    pattern: re("wv|wiedervorlage"),
    apply: (_m, r) => ((r.kind = "wiedervorlage"), true),
  },
  {
    type: "priority",
    pattern: /(?<=^|\s)(?:!hoch|!wichtig|!dringend|!!)(?=$|\s)/iu,
    apply: (_m, r) => ((r.priority = "hoch"), true),
  },
  {
    type: "priority",
    pattern: /(?<=^|\s)!niedrig(?=$|\s)/iu,
    apply: (_m, r) => ((r.priority = "niedrig"), true),
  },
  {
    type: "project",
    pattern: /(?<=^|\s)#([\p{L}\p{N}][\p{L}\p{N}._-]*)/u,
    apply: (m, r) => ((r.projectQuery = m[1]), true),
  },
  {
    type: "person",
    pattern: /(?<=^|\s)@([\p{L}\p{N}][\p{L}\p{N}._-]*)/u,
    apply: (m, r) => ((r.personQuery = m[1]), true),
  },
  // Datum vor Uhrzeit, damit "10.11." nicht als 10:11 gelesen wird.
  {
    type: "date",
    pattern: re("(?:bis\\s+|am\\s+)?(\\d{1,2})\\.(\\d{1,2})\\.(\\d{2}|\\d{4})?"),
    apply: (m, r, today) => {
      const day = Number(m[1]);
      const month = Number(m[2]) - 1;
      let year = m[3] ? Number(m[3]) : today.getFullYear();
      if (m[3] && m[3].length === 2) year = 2000 + year;
      let d = new Date(year, month, day);
      if (d.getMonth() !== month || d.getDate() !== day) return false;
      if (!m[3] && d < addDays(today, 0)) d = new Date(year + 1, month, day);
      r.dueDate = toIsoDate(d);
      return true;
    },
  },
  {
    type: "date",
    pattern: re("übermorgen|uebermorgen"),
    apply: (_m, r, today) => ((r.dueDate = toIsoDate(addDays(today, 2))), true),
  },
  {
    type: "date",
    pattern: re("heute"),
    apply: (_m, r, today) => ((r.dueDate = toIsoDate(today)), true),
  },
  {
    type: "date",
    pattern: re("morgen"),
    apply: (_m, r, today) => ((r.dueDate = toIsoDate(addDays(today, 1))), true),
  },
  {
    type: "date",
    pattern: re("in\\s+(\\d{1,3})\\s+(tag|tagen|woche|wochen|monat|monaten)"),
    apply: (m, r, today) => {
      const n = Number(m[1]);
      const unit = m[2].toLowerCase();
      let d: Date;
      if (unit.startsWith("tag")) d = addDays(today, n);
      else if (unit.startsWith("woche")) d = addDays(today, n * 7);
      else d = new Date(today.getFullYear(), today.getMonth() + n, today.getDate());
      r.dueDate = toIsoDate(d);
      return true;
    },
  },
  {
    type: "date",
    pattern: re("nächste\\s+woche|naechste\\s+woche"),
    apply: (_m, r, today) => ((r.dueDate = toIsoDate(nextWeekday(today, 1))), true),
  },
  {
    type: "date",
    pattern: re("ende\\s+der\\s+woche|ende\\s+woche|wochenende"),
    apply: (_m, r, today) => ((r.dueDate = toIsoDate(nextWeekday(today, 5, true))), true),
  },
  {
    type: "date",
    pattern: re("ende\\s+des\\s+monats|monatsende|ende\\s+monat"),
    apply: (_m, r, today) => ((r.dueDate = toIsoDate(endOfMonth(today))), true),
  },
  {
    type: "date",
    pattern: re("(?:am\\s+|nächsten\\s+|naechsten\\s+|kommenden\\s+)?(montag|dienstag|mittwoch|donnerstag|freitag|samstag|sonntag)"),
    apply: (m, r, today) => ((r.dueDate = toIsoDate(nextWeekday(today, WEEKDAYS[m[1].toLowerCase()]))), true),
  },
  {
    type: "time",
    pattern: re("(?:um\\s+)?([01]?\\d|2[0-3])[:.]([0-5]\\d)(?:\\s*uhr)?"),
    apply: (m, r) => ((r.dueTime = `${m[1].padStart(2, "0")}:${m[2]}`), true),
  },
  {
    type: "time",
    pattern: re("(?:um\\s+)?([01]?\\d|2[0-3])\\s*(?:uhr|h)"),
    apply: (m, r) => ((r.dueTime = `${m[1].padStart(2, "0")}:00`), true),
  },
  {
    type: "time",
    pattern: re("um\\s+([01]?\\d|2[0-3])"),
    apply: (m, r) => ((r.dueTime = `${m[1].padStart(2, "0")}:00`), true),
  },
];

export function parseQuickAdd(input: string, today: Date = new Date()): ParsedQuickAdd {
  const result: ParsedQuickAdd = { title: "", tokens: [] };
  let rest = ` ${input} `;
  const seen = new Set<TokenType>();

  for (const rule of RULES) {
    if (seen.has(rule.type) && rule.type !== "recurrence") continue;
    const m = rule.pattern.exec(rest);
    if (!m) continue;
    if (rule.type === "recurrence" && result.recurrence) continue;
    if (!rule.apply(m, result, today)) continue;
    seen.add(rule.type);
    result.tokens.push({ type: rule.type, text: m[0].trim() });
    rest = rest.slice(0, m.index) + " " + rest.slice(m.index + m[0].length);
  }

  // Uhrzeit ohne Datum → heute (bzw. morgen, wenn die Uhrzeit schon vorbei ist).
  if (result.dueTime && !result.dueDate) {
    const [h, min] = result.dueTime.split(":").map(Number);
    const passed = today.getHours() > h || (today.getHours() === h && today.getMinutes() >= min);
    result.dueDate = toIsoDate(addDays(today, passed ? 1 : 0));
  }
  if (result.recurrence && !result.dueDate) {
    result.dueDate = toIsoDate(today);
  }

  const title = rest
    .replace(/\s{2,}/g, " ")
    .replace(/^[\s,;:\u2013-]+|[\s,;:\u2013-]+$/g, "")
    .trim();
  result.title = title || input.trim();
  return result;
}

/** Findet das gerade getippte #- oder @-Wort am Cursor (für Vorschläge). */
export function activeMention(input: string, caret: number): { kind: "#" | "@"; query: string; start: number } | null {
  const before = input.slice(0, caret);
  const m = /(?:^|\s)([#@])([\p{L}\p{N}._-]*)$/u.exec(before);
  if (!m) return null;
  return { kind: m[1] as "#" | "@", query: m[2], start: caret - m[2].length - 1 };
}
