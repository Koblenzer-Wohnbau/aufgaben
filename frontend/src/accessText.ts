/** Texte für das Zugriffsprotokoll der Einsichtsrechte (Verwaltung und Einstellungen). */
export const ACCESS_ACTION_LABEL: Record<string, string> = {
  angelegt: "hat die Freigabe angelegt",
  angesehen: "hat die Aufgabenliste angesehen",
  geoeffnet: "hat eine Aufgabe geöffnet",
  geaendert: "hat eine Aufgabe geändert",
  bestaetigt: "hat die Freigabe bestätigt",
  widerrufen: "hat die Freigabe widerrufen",
};

export function accessActionLabel(action: string): string {
  return ACCESS_ACTION_LABEL[action] ?? action;
}
