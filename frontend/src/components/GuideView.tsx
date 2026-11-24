import { useEffect, useRef, useState, type ReactNode } from "react";
import { Button, Field, Input, makeStyles, mergeClasses, Text, tokens } from "@fluentui/react-components";
import {
  ArrowLeftRegular, ArrowRightRegular, BookOpenRegular, CalendarLtrRegular, CheckmarkCircleRegular,
  CircleRegular, MailRegular, PeopleRegular, StarFilled, StarRegular,
} from "@fluentui/react-icons";
import { useApp } from "../context";
import { longDate } from "../dates";
import { parseQuickAdd } from "../quickadd";
import { hrefFor } from "../route";
import type { Me } from "../types";

type Step = [action: string, detail: string];
type Exercise = { prompt: string; action: string; result: string; icon: "check" | "star" | "date" | "mail" | "person" };
type Chapter = {
  id: string;
  label: string;
  title: string;
  intro: string;
  steps: Step[];
  example?: { label: string; lines: string[] };
  exercise?: Exercise;
};

function chaptersFor(me: Me): Chapter[] {
  const mailDestination = me.showInbox ? "Aufgaben" : "Geplant";
  return [
    {
      id: "start", label: "Mein Tag", title: "Was steht heute an?",
      intro: "In „Mein Tag“ siehst du, was heute fällig oder bereits überfällig ist. Du kannst weitere Aufgaben für heute dazunehmen.",
      steps: [
        ["Aufgabe hinzufügen", "Gib einen Titel ein und drücke Enter. Mit n springst du direkt in die Eingabe."],
        ["Vorschläge öffnen", "Hol dir mit dem Plus eine offene Aufgabe in deinen Tag. Ihre Fälligkeit bleibt erhalten."],
        ["Erledigen", "Klicke den Kreis links neben einer Aufgabe an. Einen versehentlichen Klick kannst du dort rückgängig machen."],
      ],
      exercise: { prompt: "Protokoll gegenlesen", action: "Beispielaufgabe abhaken", result: "Erledigt. Dein Fortschritt für heute ist aktualisiert.", icon: "check" },
    },
    {
      id: "ansichten", label: "Wichtig und Geplant", title: "Finde die passende Ansicht",
      intro: "Eine Aufgabe kann gleichzeitig in mehreren Ansichten stehen. Wenn du sie bearbeitest, ändert sich überall dieselbe Aufgabe.",
      steps: [
        ["Wichtig", "Sterne markieren Aufgaben mit hoher Priorität. Du findest sie gesammelt unter „Wichtig“."],
        ["Geplant", me.showInbox ? "Hier stehen deine offenen Aufgaben mit Fälligkeit, nach Tagen gruppiert." : "Hier stehen deine offenen Aufgaben mit Fälligkeit. Lose Aufgaben ohne Datum findest du unter „Ohne Termin“."],
        ...(me.showInbox ? [["Aufgaben", "Hier siehst du deine Aufgaben, die in keiner persönlichen Liste liegen."]] satisfies Step[] : []),
      ],
      exercise: { prompt: "Freigabe für den Rollout", action: "Als wichtig markieren", result: "Der Stern ist gesetzt. Die Aufgabe erscheint jetzt auch unter „Wichtig“.", icon: "star" },
    },
    {
      id: "erfassen", label: "Schnelleingabe", title: "Titel, Termin und Person in einer Zeile",
      intro: "Die Eingabe erkennt Angaben wie morgen, Freitag, 10 Uhr, @Name, !hoch und wv.",
      steps: [
        ["In einem Satz schreiben", "Tippe den Arbeitsauftrag und ergänze, was du schon weißt."],
        ["Erkannte Angaben prüfen", "Unter dem Feld erscheinen Vorschläge für Datum, Uhrzeit und Person. Bei @ kannst du eine Person aus der Liste auswählen."],
        ["Mit Enter speichern", "Klicke die neue Aufgabe an, um Notizen, Checkliste, Anhänge oder eine Erinnerung zu ergänzen."],
      ],
    },
    {
      id: "fristen", label: "Fristen und Erinnerungen", title: "Lege fest, wann du etwas brauchst",
      intro: "Eine Fälligkeit sagt, bis wann etwas erledigt sein soll. Eine Erinnerung legt fest, wann du einen Hinweis bekommst.",
      steps: [
        ["Fälligkeit setzen", "Öffne die Aufgabe und wähle unter „Fällig am“ Datum und bei Bedarf Uhrzeit."],
        ["Erinnerung eintragen", "Wähle einen Zeitpunkt, zu dem du an die Aufgabe erinnert werden willst."],
        ["Wiedervorlage oder Wiederholung wählen", "Eine Wiedervorlage sammelt Dinge zum Nachfassen. Eine Wiederholung erzeugt nach dem Erledigen den nächsten Termin."],
      ],
      exercise: { prompt: "Freigabe nachfragen", action: "Für heute planen", result: "Heute ist als Fälligkeit gesetzt. Die Aufgabe wäre in „Geplant“ und in „Mein Tag“ sichtbar.", icon: "date" },
    },
    {
      id: "mail", label: "Mail weiterleiten", title: "Mach aus einer Mail eine Aufgabe",
      intro: "Die App übernimmt den Betreff als Titel, den Mailtext als Notiz und die unterstützten Anhänge in die Aufgabe.",
      steps: [
        ["Aufgaben-Adresse kopieren", me.mailInboxAddress ? "Verwende die eingeblendete Adresse und öffne in Outlook die gewünschte Mail." : "Wenn das Aufgaben-Postfach eingerichtet ist, steht die Adresse unter „Einstellungen → Mail an Aufgabe“."],
        ["In Outlook weiterleiten", "Setze die Aufgaben-Adresse als Empfänger ein. Schreib die Frist in den Betreff oder in deinen Text oberhalb der Originalmail."],
        ["Aufgabe öffnen", me.showInbox
          ? "Eigene weitergeleitete Mails findest du in „Aufgaben“. Mit Fälligkeit stehen sie auch in „Geplant“."
          : "Eigene weitergeleitete Mails findest du in „Geplant“. Fehlt die Frist, trägt die App das Datum des Eingangs ein. Du kannst es ändern."],
      ],
      example: { label: "Beispiel für den Betreff", lines: ["WG: Angebot prüfen bis Freitag !hoch", "Fällig: Freitag · Priorität: hoch"] },
      exercise: { prompt: "WG: Angebot prüfen", action: "Weiterleitung ausprobieren", result: me.showInbox
        ? `Beispiel erfasst. „Angebot prüfen“ wäre unter „${mailDestination}“ sichtbar.`
        : `Beispiel erfasst. „Angebot prüfen“ wäre unter „${mailDestination}“ mit der Fälligkeit heute sichtbar.`, icon: "mail" },
    },
    {
      id: "delegieren", label: "Zuweisen und nachhalten", title: "Wer kümmert sich darum?",
      intro: "Delegierte Aufgaben haben eine verantwortliche Person. Beide Seiten sehen den aktuellen Stand.",
      steps: [
        ["Person einsetzen", "Tippe @Name in der Schnelleingabe oder ändere in einer geöffneten Aufgabe „Verantwortlich“."],
        ["Auftrag übergeben", "Die Person erhält je nach Einstellung eine Benachrichtigung und findet die Aufgabe unter „Mir zugewiesen“."],
        ["Rückmeldung verfolgen", "Unter „Wartet auf andere“ siehst du Aufgaben, die du anderen gegeben hast."],
      ],
      exercise: { prompt: "Termin für die Abstimmung finden", action: "An Alex zuweisen", result: "Alex ist verantwortlich. Du würdest den Auftrag unter „Wartet auf andere“ wiederfinden.", icon: "person" },
    },
    {
      id: "bearbeiten", label: "Aufgabe bearbeiten", title: "Alles zur Aufgabe an einem Ort",
      intro: "Ein Klick auf die Zeile öffnet die Aufgaben-Details.",
      steps: [
        ["Notizen und Teilaufgaben ergänzen", "Halte den Hintergrund in den Notizen fest und teile größere Arbeiten in Checklistenpunkte."],
        ["Dateien hinzufügen", "Dateien lassen sich auswählen oder hineinziehen. Bilder aus der Zwischenablage kannst du mit Strg+V hinzufügen."],
        ["Auf Antwort warten oder zurückstellen", "Unter „Warte auf …“ kannst du festhalten, von wem eine Antwort fehlt. Mit „Später“ legst du dir die Aufgabe wieder vor."],
        ["Aufgabenverlauf ansehen", "Im Verlauf siehst du Änderungen und Kommentare. Mit Esc schließt du die Details."],
      ],
    },
    {
      id: "listen", label: "Listen", title: "Halte zusammen, was zusammengehört",
      intro: "Eine Liste eignet sich für ein Thema, zum Beispiel die nächste Teams-Schulung oder die Büroausstattung.",
      steps: [
        ["Liste anlegen", "Klicke neben „Listen“ auf das Plus und gib einen Namen ein."],
        ["Aufgaben einsortieren", "Lege sie in der geöffneten Liste an oder ziehe eine vorhandene Aufgabe auf die Liste in der Seitenleiste."],
        ["Bei Bedarf teilen", "Öffne „Teilen“ in der Liste und wähle die Kolleginnen und Kollegen aus, die sie sehen und mitbearbeiten sollen."],
      ],
    },
    ...(me.aiAvailable ? [{
      id: "ki", label: "Mit KI erfassen", title: "Aus Text werden Aufgabenentwürfe",
      intro: "Aus einem Text oder Bildschirmfoto kann die App eine oder mehrere Aufgaben vorschlagen.",
      steps: [
        ["Inhalt übernehmen", "Kopiere einen Text oder einen Screenshot. Klicke auf „KI“ oder füge den Inhalt mit Strg+V außerhalb eines Eingabefelds ein."],
        ["Vorschläge prüfen", "Prüfe Titel, Verantwortliche und Fristen und ergänze, was fehlt."],
        ["Auswahl bestätigen", "Markiere die Vorschläge, die du tatsächlich anlegen möchtest."],
      ] satisfies Step[],
    }] : []),
    {
      id: "finden", label: "Suche und Papierkorb", title: "Finde Aufgaben wieder",
      intro: "Die Suche findet Wörter in Titeln und Notizen. Sie hilft auch, wenn eine Aufgabe zurückgestellt oder in einer anderen Ansicht steht.",
      steps: [
        ["Suchen", "Drücke / oder verwende das Suchfeld. Gib einen Begriff ein und drücke Enter."],
        ["Erledigte ansehen", "Unter „Erledigt“ findest du erledigte Aufgaben aus den letzten 30 Tagen."],
        ["Papierkorb prüfen", "Gelöschte Aufgaben bleiben 30 Tage im Papierkorb. Dort kannst du sie wiederherstellen."],
      ],
    },
    {
      id: "vertretung", label: "Urlaub und Vertretung", title: "Übergib, was während deines Urlaubs fällig wird",
      intro: "Wähle Zeitraum und Vertretung. Die App hilft dir, die in dieser Zeit fälligen Aufgaben durchzugehen.",
      steps: [
        ["Urlaub eintragen", "Öffne „Urlaub und Vertretung“ und trage Zeitraum und Vertretung ein."],
        ["Aufgaben durchgehen", "Entscheide pro Aufgabe, ob sie übergeben oder bis nach dem Urlaub zurückgestellt werden soll."],
        ["Rückkehr prüfen", "„Mein Tag“ zeigt nach dem Urlaub, was passiert ist. Offene Aufgaben kannst du zurückholen."],
      ],
    },
    {
      id: "einstellungen", label: "Einstellungen", title: "Richte Aufgaben für dich ein",
      intro: "Die Einstellungen findest du über das Zahnrad unten in der Seitenleiste.",
      steps: [
        ["Benachrichtigungen einstellen", "Wähle Teams, E-Mail oder „Automatisch“, je nachdem, was bei euch eingerichtet ist. Die Tagesübersicht kannst du separat umstellen."],
        ["Kalender abonnieren", "Erzeuge bei Bedarf deinen persönlichen Outlook-Kalenderlink und füge ihn in Outlook als Internetkalender hinzu."],
        ["Freigaben ansehen", "Unter „Wer darf meine Aufgaben sehen?“ siehst du deine erteilten Einsichtsrechte und letzte Zugriffe."],
        ["Anleitung erneut öffnen", "Klicke in der Seitenleiste oder in den Einstellungen auf „Anleitung“ und springe zum gewünschten Kapitel."],
      ],
    },
  ];
}

const useStyles = makeStyles({
  view: { flex: 1, overflowY: "auto", minWidth: 0, color: tokens.colorNeutralForeground1 },
  heading: {
    display: "flex", alignItems: "center", gap: "10px", margin: 0,
    padding: "22px 28px 14px", fontSize: tokens.fontSizeBase500, fontWeight: tokens.fontWeightSemibold,
    "@media (max-width: 760px)": { padding: "16px 16px 12px 56px" },
  },
  headingIcon: { color: tokens.colorBrandForeground1, fontSize: "22px" },
  layout: {
    display: "grid", gridTemplateColumns: "minmax(0, 1fr) 192px", gap: "18px",
    maxWidth: "1150px", margin: "0 auto", padding: "0 28px 28px",
    "@media (max-width: 1050px)": { gridTemplateColumns: "minmax(0, 1fr)" },
    "@media (max-width: 760px)": { padding: "0 12px 16px", gap: "12px" },
  },
  slide: {
    overflow: "hidden", border: `1px solid ${tokens.colorNeutralStroke2}`, borderRadius: "14px",
    backgroundColor: tokens.colorNeutralBackground1, boxShadow: tokens.shadow4,
  },
  banner: {
    display: "grid", gridTemplateColumns: "minmax(0, 1fr)", gap: "12px",
    padding: "clamp(18px, 4vw, 34px)",
    backgroundColor: tokens.colorBrandBackground2,
  },
  label: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorNeutralForeground1 },
  title: {
    margin: 0, maxWidth: "22ch", color: tokens.colorNeutralForeground1,
    fontSize: "clamp(26px, 4vw, 38px)", lineHeight: 1.12, fontWeight: tokens.fontWeightSemibold,
    overflowWrap: "anywhere",
  },
  intro: { maxWidth: "60ch", color: tokens.colorNeutralForeground2, lineHeight: "23px" },
  content: {
    display: "grid", gridTemplateColumns: "minmax(0, 1fr) minmax(185px, 0.8fr)", gap: "24px",
    padding: "clamp(16px, 4vw, 30px)",
    "@media (max-width: 1140px)": { gridTemplateColumns: "minmax(0, 1fr)" },
  },
  steps: { listStyleType: "none", margin: 0, padding: 0, display: "grid", gap: "14px" },
  step: { display: "grid", gridTemplateColumns: "26px minmax(0, 1fr)", gap: "10px", alignItems: "start" },
  marker: {
    display: "grid", placeItems: "center", width: "24px", height: "24px",
    borderRadius: "50%", backgroundColor: tokens.colorBrandBackground2, color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightSemibold,
  },
  stepDetail: { color: tokens.colorNeutralForeground2, lineHeight: "21px" },
  side: { minWidth: 0, display: "grid", alignContent: "start", gap: "12px" },
  example: {
    display: "grid", gap: "8px", padding: "14px",
    backgroundColor: tokens.colorNeutralBackground2, borderRadius: tokens.borderRadiusLarge,
    border: `1px solid ${tokens.colorNeutralStroke2}`, overflowWrap: "anywhere",
  },
  practiceRow: {
    display: "flex", alignItems: "center", gap: "8px", padding: "9px",
    borderRadius: tokens.borderRadiusMedium, backgroundColor: tokens.colorNeutralBackground1,
  },
  practiceIcon: { flexShrink: 0, fontSize: "20px", color: tokens.colorBrandForeground1 },
  feedback: { lineHeight: "20px", minHeight: "20px" },
  address: { display: "flex", flexWrap: "wrap", gap: "8px", alignItems: "center", overflowWrap: "anywhere" },
  footer: {
    display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: "8px",
    padding: "12px clamp(12px, 4vw, 30px)", borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  chapters: {
    display: "grid", gap: "2px", alignContent: "start", padding: "8px 0",
    "@media (max-width: 1050px)": { display: "flex", flexWrap: "wrap", padding: 0 },
  },
  chapter: {
    padding: "8px 10px", color: tokens.colorNeutralForeground2, textDecorationLine: "none",
    borderRadius: tokens.borderRadiusMedium, ":hover": { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  active: { color: tokens.colorBrandForeground1, backgroundColor: tokens.colorNeutralBackground1, fontWeight: tokens.fontWeightSemibold },
});

function practiceIcon(icon: Exercise["icon"], done: boolean): ReactNode {
  switch (icon) {
    case "check": return done ? <CheckmarkCircleRegular /> : <CircleRegular />;
    case "star": return done ? <StarFilled /> : <StarRegular />;
    case "date": return <CalendarLtrRegular />;
    case "mail": return <MailRegular />;
    case "person": return <PeopleRegular />;
  }
}

function Practice({ exercise, today }: { exercise: Exercise; today: string }) {
  const styles = useStyles();
  const [done, setDone] = useState(false);
  return (
    <div className={styles.example}>
      <Text weight="semibold">Hier ausprobieren</Text>
      <div className={styles.practiceRow}>
        <span className={styles.practiceIcon} aria-hidden="true">{practiceIcon(exercise.icon, done)}</span>
        <div>
          <Text block strikethrough={done && exercise.icon === "check"}>{exercise.prompt}</Text>
          {done && exercise.icon === "date" && <Text size={200}>{longDate(today)}</Text>}
          {done && exercise.icon === "person" && <Text size={200}>Verantwortlich: Alex</Text>}
        </div>
      </div>
      <Button onClick={() => setDone(!done)}>{done ? "Beispiel zurücksetzen" : exercise.action}</Button>
      <Text size={200} className={styles.feedback} role="status">{done ? exercise.result : "Übung mit Beispieldaten."}</Text>
    </div>
  );
}

function QuickAddPractice({ today }: { today: string }) {
  const styles = useStyles();
  const [input, setInput] = useState("Unterlagen prüfen morgen 10 Uhr !hoch");
  const parsed = parseQuickAdd(input, new Date(`${today}T12:00:00`));
  const details = [
    parsed.dueDate && `Fällig am ${longDate(parsed.dueDate)}${parsed.dueTime ? `, ${parsed.dueTime} Uhr` : ""}`,
    parsed.priority && `Priorität ${parsed.priority}`,
    parsed.personQuery && `Person @${parsed.personQuery}`,
    parsed.kind && "Wiedervorlage",
  ].filter(Boolean);
  return (
    <div className={styles.example}>
      <Field label="Schnelleingabe ausprobieren" hint="Ändere „morgen“ zu „Freitag“ und sieh dir das Ergebnis an.">
        <Input value={input} onChange={(_, data) => setInput(data.value)} />
      </Field>
      <div className={styles.example} aria-live="polite">
        {input.trim() ? (
          <>
            <Text weight="semibold">{parsed.title}</Text>
            {details.map((detail) => <Text key={detail}>{detail}</Text>)}
          </>
        ) : <Text>Gib einen Titel und zum Beispiel eine Frist ein.</Text>}
      </div>
    </div>
  );
}

const chapterHref = (chapter: Chapter) => hrefFor({ page: "anleitung", topic: chapter.id });

export function GuideView({ topic }: { topic?: string }) {
  const styles = useStyles();
  const { me, notify } = useApp();
  const chapters = chaptersFor(me);
  const index = Math.max(0, chapters.findIndex((c) => c.id === topic));
  const chapter = chapters[index];
  const previous = chapters[index - 1];
  const next = chapters[index + 1];
  const heading = useRef<HTMLHeadingElement>(null);
  const view = useRef<HTMLDivElement>(null);
  useEffect(() => {
    view.current?.scrollTo({ top: 0 });
    heading.current?.focus({ preventScroll: true });
  }, [chapter.id]);

  const copyAddress = async () => {
    if (!me.mailInboxAddress) return;
    try {
      await navigator.clipboard.writeText(me.mailInboxAddress);
      notify("Aufgaben-Adresse kopiert");
    } catch {
      notify("Kopieren fehlgeschlagen. Du kannst die Adresse im Text markieren.", "error");
    }
  };

  return (
    <div className={styles.view} ref={view}>
      <h1 className={styles.heading}><BookOpenRegular className={styles.headingIcon} /> Anleitung</h1>
      <div className={styles.layout}>
        <article className={styles.slide} aria-labelledby="guide-chapter">
          <header className={styles.banner}>
            <Text className={styles.label}>{chapter.label}</Text>
            <h2 className={styles.title} id="guide-chapter" ref={heading} tabIndex={-1}>{chapter.title}</h2>
            <Text className={styles.intro}>{chapter.intro}</Text>
          </header>
          <div className={styles.content} style={chapter.id !== "erfassen" && !chapter.exercise ? { gridTemplateColumns: "minmax(0, 1fr)" } : undefined}>
            <ol className={styles.steps} role="list">
              {chapter.steps.map(([action, detail], i) => (
                <li key={action} className={styles.step}>
                  <span className={styles.marker} aria-hidden="true">{i + 1}</span>
                  <div>
                    <Text block weight="semibold">{action}</Text>
                    <Text block className={styles.stepDetail}>{detail}</Text>
                  </div>
                </li>
              ))}
            </ol>
            <div className={styles.side}>
              {chapter.id === "erfassen" && <QuickAddPractice today={me.today} />}
              {chapter.id === "mail" && me.mailInboxAddress && (
                <div className={styles.example}>
                  <Text weight="semibold">Dein Aufgaben-Postfach</Text>
                  <div className={styles.address}>
                    <Text>{me.mailInboxAddress}</Text>
                    <Button size="small" onClick={() => void copyAddress()}>Adresse kopieren</Button>
                  </div>
                </div>
              )}
              {chapter.example && (
                <div className={styles.example}>
                  <Text weight="semibold">{chapter.example.label}</Text>
                  {chapter.example.lines.map((line) => <Text key={line}>{line}</Text>)}
                </div>
              )}
              {chapter.exercise && <Practice key={chapter.id} exercise={chapter.exercise} today={me.today} />}
            </div>
          </div>
          <footer className={styles.footer}>
            {previous ? (
              <Button as="a" href={chapterHref(previous)} icon={<ArrowLeftRegular />}>Zurück</Button>
            ) : <span />}
            <Text size={200} aria-label={`Kapitel ${index + 1} von ${chapters.length}`}>{index + 1} / {chapters.length}</Text>
            {next ? (
              <Button as="a" appearance="primary" href={chapterHref(next)} icon={<ArrowRightRegular />} iconPosition="after">Weiter</Button>
            ) : (
              <Button as="a" appearance="primary" href={hrefFor({ page: "tasks", view: "mein-tag" })}>Zu „Mein Tag“</Button>
            )}
          </footer>
        </article>
        <nav className={styles.chapters} aria-label="Kapitel der Anleitung">
          {chapters.map((c) => (
            <a key={c.id} className={mergeClasses(styles.chapter, c.id === chapter.id && styles.active)}
              href={chapterHref(c)} aria-current={c.id === chapter.id ? "step" : undefined}>{c.label}</a>
          ))}
        </nav>
      </div>
    </div>
  );
}
