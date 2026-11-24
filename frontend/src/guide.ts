import type { Me } from "./types";

export const GUIDE_ACTIONS = {
  start: { view: "mein-tag", label: "Erste Aufgabe anlegen", hint: "Tippe in „Neue Aufgabe“ einen Titel und drücke Enter." },
  fristen: { view: "geplant", label: "Fälligkeit setzen", hint: "Öffne eine Aufgabe und setze unter „Fällig am“ ein Datum." },
  ansichten: { view: "geplant", label: "Geplant öffnen", hint: "Hier siehst du offene Fälligkeiten. Ein Stern an einer Aufgabe zeigt sie auch unter „Wichtig“." },
  erfassen: { view: "mein-tag", label: "Schnelleingabe öffnen", hint: "Tippe zum Titel eine Frist wie „morgen 10 Uhr“. Prüfe die Vorschau vor dem Speichern." },
  mail: { view: "geplant", label: "Mail an Aufgabe öffnen", hint: "Leite in Outlook eine Mail an die Aufgaben-Adresse weiter. Sobald sie übernommen ist, erscheint die Aufgabe hier." },
  delegieren: { view: "delegiert", label: "Eine Aufgabe zuweisen", hint: "Schreibe einen Titel und wähle mit @ die Person aus, der du die Aufgabe zuweisen möchtest." },
  bearbeiten: { view: "geplant", label: "Aufgabe bearbeiten", hint: "Öffne eine Aufgabe, um Notizen oder Checklistenpunkte zu ergänzen." },
  listen: { view: "mein-tag", label: "Liste anlegen", hint: "Öffne die Seitenleiste und klicke auf das Plus neben „Listen“." },
  ki: { view: "mein-tag", label: "KI-Erfassung öffnen", hint: "Füge Text oder einen Screenshot ein und prüfe den Entwurf vor dem Anlegen." },
  finden: { view: "suche", label: "Suche öffnen", hint: "Gib oben im Suchfeld ein Wort aus dem Titel oder den Notizen ein." },
  vertretung: { view: "mein-tag", label: "Urlaubsplanung öffnen", hint: "Wähle im Dialog den Zeitraum und deine Vertretung." },
  einstellungen: { view: "mein-tag", label: "Einstellungen öffnen", hint: "Im Zahnrad-Menü kannst du Benachrichtigungen und den Kalender einstellen." },
} as const;

export type GuideTopic = keyof typeof GUIDE_ACTIONS;

type Step = [action: string, detail: string];
export type Chapter = {
  id: GuideTopic;
  label: string;
  title: string;
  intro: string;
  steps: Step[];
};

export function chaptersFor(me: Me): Chapter[] {
  return [
    {
      id: "start", label: "Mein Tag", title: "Was steht heute an?",
      intro: "Hier stehen fällige Aufgaben und alles, was du für heute eingeplant hast.",
      steps: [
        ["Aufgabe hinzufügen", "Titel eingeben und Enter drücken."],
        ["Vorschläge öffnen", "Mit dem Plus eine Aufgabe dazunehmen."],
        ["Erledigen", "Kreis links neben der Aufgabe anklicken."],
      ],
    },
    {
      id: "fristen", label: "Fristen und Erinnerungen", title: "Lege fest, wann du etwas brauchst",
      intro: "Öffne eine Aufgabe, um ihre Termine zu ändern.",
      steps: [
        ["Fällig am", "Bis wann soll die Aufgabe fertig sein?"],
        ["Erinnerung", "Wann möchtest du einen Hinweis bekommen?"],
        ["Wiedervorlage und Wiederholung", "Etwas nachfassen oder den nächsten Termin automatisch anlegen."],
      ],
    },
    {
      id: "ansichten", label: "Wichtig und Geplant", title: "Finde die passende Ansicht",
      intro: "Eine Aufgabe kann in mehreren Ansichten auftauchen.",
      steps: [
        ["Wichtig", "Deine Aufgaben mit Stern."],
        ["Geplant", me.showInbox ? "Deine offenen Fälligkeiten." : "Deine offenen Fälligkeiten und lose Aufgaben unter „Ohne Termin“."],
        ...(me.showInbox ? [["Aufgaben", "Deine Aufgaben ohne Liste."]] satisfies Step[] : []),
      ],
    },
    {
      id: "erfassen", label: "Schnelleingabe", title: "Titel, Termin und Person in einer Zeile",
      intro: "Schreib Titel und Frist einfach zusammen.",
      steps: [
        ["Termin angeben", "Zum Beispiel „morgen 10 Uhr“."],
        ["Person und Priorität ergänzen", "@Name weist zu. !hoch markiert als wichtig."],
        ["Mit Enter speichern", "Vorher die erkannten Angaben unter dem Feld prüfen."],
      ],
    },
    {
      id: "mail", label: "Mail weiterleiten", title: "Mach aus einer Mail eine Aufgabe",
      intro: "Betreff, Mailtext und Anhänge werden zur Aufgabe.",
      steps: [
        ["In Outlook weiterleiten", me.mailInboxAddress ? "Aufgaben-Adresse kopieren und als Empfänger einsetzen." : "Die Adresse steht nach Einrichtung unter „Einstellungen → Mail an Aufgabe“."],
        ["Frist ergänzen", "Zum Beispiel „bis Freitag“ in den Betreff schreiben."],
        ["Aufgabe öffnen", me.showInbox
          ? "Unter „Aufgaben“, mit Fälligkeit auch unter „Geplant“."
          : "Unter „Geplant“. Ohne Frist setzt die App beim Abruf das Tagesdatum."],
      ],
    },
    {
      id: "delegieren", label: "Zuweisen und nachhalten", title: "Wer kümmert sich darum?",
      intro: "Wenn du eine Aufgabe delegierst, sehen beide Seiten den Stand.",
      steps: [
        ["Zuweisen", "@Name tippen oder „Verantwortlich“ ändern."],
        ["Mir zugewiesen", "Hier sieht die andere Person den Auftrag."],
        ["Wartet auf andere", "Hier verfolgst du delegierte Aufgaben."],
      ],
    },
    {
      id: "bearbeiten", label: "Aufgabe bearbeiten", title: "Alles zur Aufgabe an einem Ort",
      intro: "Klicke auf die Aufgabe, um ihre Details zu öffnen.",
      steps: [
        ["Notiz und Checkliste", "Hintergrund festhalten und Teilschritte abhaken."],
        ["Anhänge", "Dateien auswählen, hineinziehen oder Bilder mit Strg+V einfügen."],
        ["Warte auf … und Später", "Auf eine Antwort warten oder die Aufgabe zurückstellen."],
      ],
    },
    {
      id: "listen", label: "Listen", title: "Halte zusammen, was zusammengehört",
      intro: "Eine Liste bündelt Aufgaben zu einem Thema.",
      steps: [
        ["Liste anlegen", "Plus neben „Listen“ anklicken."],
        ["Aufgabe einsortieren", "Direkt anlegen oder auf die Liste ziehen."],
        ["Liste teilen", "Über „Teilen“ Personen zur Mitarbeit einladen."],
      ],
    },
    ...(me.aiAvailable ? [{
      id: "ki" as const, label: "Mit KI erfassen", title: "Aus Text werden Aufgabenentwürfe",
      intro: "Die KI macht aus Text oder einem Screenshot einen Entwurf.",
      steps: [
        ["Inhalt einfügen", "„KI“ anklicken oder außerhalb eines Felds Strg+V drücken."],
        ["Entwurf prüfen", "Titel, Person und Frist kontrollieren."],
        ["Aufgaben anlegen", "Passende Vorschläge auswählen und bestätigen."],
      ] satisfies Step[],
    }] : []),
    {
      id: "finden", label: "Suche und Papierkorb", title: "Finde Aufgaben wieder",
      intro: "Auch zurückgestellte und gelöschte Aufgaben lassen sich wiederfinden.",
      steps: [
        ["Suchen", "Mit / starten. Die Suche findet Titel und Notizen."],
        ["Erledigt", "Zeigt die letzten 30 Tage."],
        ["Papierkorb", "Aufgaben lassen sich 30 Tage lang wiederherstellen."],
      ],
    },
    ...(me.absencesEnabled ? [{
      id: "vertretung" as const, label: "Urlaub und Vertretung", title: "Übergib, was während deines Urlaubs fällig wird",
      intro: "Plane, wer sich während deiner Abwesenheit kümmert.",
      steps: [
        ["Urlaub eintragen", "Zeitraum und Vertretung wählen."],
        ["Aufgaben einplanen", "Übergeben oder bis zur Rückkehr zurückstellen."],
        ["Aufgaben zurückholen", "Nach dem Urlaub die Übersicht in „Mein Tag“ öffnen."],
      ] satisfies Step[],
    }] : []),
    {
      id: "einstellungen", label: "Einstellungen", title: "Richte Aufgaben für dich ein",
      intro: "Das Zahnrad unten öffnet deine Einstellungen.",
      steps: [
        ["Benachrichtigungen", "Kanal und Tagesübersicht einstellen."],
        ["Outlook-Kalender", "Persönlichen Kalenderlink erstellen und in Outlook abonnieren."],
        ["Einsichtsrechte", "Erteilte Freigaben und letzte Zugriffe ansehen."],
      ],
    },
  ];
}
