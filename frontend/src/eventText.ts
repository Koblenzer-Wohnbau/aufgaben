/** Texte für den Verlauf einer Aufgabe, im Detailbereich und in „Was ist passiert?“. */
export const EVENT_TEXT: Record<string, string> = {
  angelegt: "hat die Aufgabe angelegt",
  zugewiesen: "hat zugewiesen",
  erledigt: "hat erledigt",
  "wieder-geoeffnet": "hat wieder geöffnet",
  termin: "hat den Termin geändert:",
  folgeaufgabe: "hat die Folgeaufgabe angelegt,",
  kommentar: "",
  anhang: "hat angehängt:",
  "anhang-entfernt": "hat den Anhang entfernt:",
  warten: "wartet",
  "antwort-da": "hat die Antwort erhalten von",
  zurueckgestellt: "hat zurückgestellt",
  zurueckgeholt: "hat die Aufgabe zurückgeholt",
  papierkorb: "hat in den Papierkorb gelegt",
  wiederhergestellt: "hat aus dem Papierkorb wiederhergestellt",
  vertretung: "hat an die Urlaubsvertretung übergeben:",
  "vertretung-zurueck": "hat die Aufgabe von der Urlaubsvertretung zurückgenommen,",
};

/** Was passiert ist, ohne die Person davor, Kommentare stehen im Verlauf für sich, hier bleibt die Zeile leer. */
export function eventLine(e: { kind: string; text: string }): string {
  if (e.kind === "angelegt" && e.text) return `hat die Aufgabe ${e.text} angelegt`;
  if (e.kind === "kommentar") return "";
  return `${EVENT_TEXT[e.kind] ?? e.kind} ${e.text}`.trim();
}
