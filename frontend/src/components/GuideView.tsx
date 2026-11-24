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
  exercise?: Exercise;
};

function chaptersFor(me: Me): Chapter[] {
  const mailDestination = me.showInbox ? "Aufgaben" : "Geplant";
  return [
    {
      id: "start", label: "Mein Tag", title: "Was steht heute an?",
      intro: "Hier stehen fällige Aufgaben und alles, was du für heute eingeplant hast.",
      steps: [
        ["Aufgabe hinzufügen", "Titel eingeben und Enter drücken."],
        ["Vorschläge öffnen", "Mit dem Plus eine Aufgabe dazunehmen."],
        ["Erledigen", "Kreis links neben der Aufgabe anklicken."],
      ],
      exercise: { prompt: "Protokoll gegenlesen", action: "Beispielaufgabe abhaken", result: "Erledigt. Dein Fortschritt für heute ist aktualisiert.", icon: "check" },
    },
    {
      id: "ansichten", label: "Wichtig und Geplant", title: "Finde die passende Ansicht",
      intro: "Eine Aufgabe kann in mehreren Ansichten auftauchen.",
      steps: [
        ["Wichtig", "Deine Aufgaben mit Stern."],
        ["Geplant", me.showInbox ? "Deine offenen Fälligkeiten." : "Deine offenen Fälligkeiten und lose Aufgaben unter „Ohne Termin“."],
        ...(me.showInbox ? [["Aufgaben", "Deine Aufgaben ohne Liste."]] satisfies Step[] : []),
      ],
      exercise: { prompt: "Freigabe für den Rollout", action: "Als wichtig markieren", result: "Der Stern ist gesetzt. Die Aufgabe erscheint jetzt auch unter „Wichtig“.", icon: "star" },
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
      id: "fristen", label: "Fristen und Erinnerungen", title: "Lege fest, wann du etwas brauchst",
      intro: "Öffne eine Aufgabe, um ihre Termine zu ändern.",
      steps: [
        ["Fällig am", "Bis wann soll die Aufgabe fertig sein?"],
        ["Erinnerung", "Wann möchtest du einen Hinweis bekommen?"],
        ["Wiedervorlage und Wiederholung", "Etwas nachfassen oder den nächsten Termin automatisch anlegen."],
      ],
      exercise: { prompt: "Freigabe nachfragen", action: "Für heute planen", result: "Heute ist als Fälligkeit gesetzt. Die Aufgabe wäre in „Geplant“ und in „Mein Tag“ sichtbar.", icon: "date" },
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
      exercise: { prompt: "WG: Angebot prüfen", action: "Weiterleitung ausprobieren", result: me.showInbox
        ? `Beispiel erfasst. „Angebot prüfen“ wäre unter „${mailDestination}“ sichtbar.`
        : `Beispiel erfasst. „Angebot prüfen“ wäre unter „${mailDestination}“ mit der Fälligkeit heute sichtbar.`, icon: "mail" },
    },
    {
      id: "delegieren", label: "Zuweisen und nachhalten", title: "Wer kümmert sich darum?",
      intro: "Wenn du eine Aufgabe delegierst, sehen beide Seiten den Stand.",
      steps: [
        ["Zuweisen", "@Name tippen oder „Verantwortlich“ ändern."],
        ["Mir zugewiesen", "Hier sieht die andere Person den Auftrag."],
        ["Wartet auf andere", "Hier verfolgst du delegierte Aufgaben."],
      ],
      exercise: { prompt: "Termin für die Abstimmung finden", action: "An Alex zuweisen", result: "Alex ist verantwortlich. Du würdest den Auftrag unter „Wartet auf andere“ wiederfinden.", icon: "person" },
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
      id: "ki", label: "Mit KI erfassen", title: "Aus Text werden Aufgabenentwürfe",
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
      id: "vertretung", label: "Urlaub und Vertretung", title: "Übergib, was während deines Urlaubs fällig wird",
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

const useStyles = makeStyles({
  view: { flex: 1, overflowY: "auto", minWidth: 0, color: tokens.colorNeutralForeground1 },
  heading: {
    display: "flex", alignItems: "center", gap: "10px", margin: 0,
    padding: "22px 28px 14px", fontSize: tokens.fontSizeBase600, fontWeight: tokens.fontWeightSemibold,
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
    fontSize: "clamp(30px, 4vw, 44px)", lineHeight: 1.12, fontWeight: tokens.fontWeightSemibold,
    overflowWrap: "anywhere",
  },
  intro: { maxWidth: "52ch", color: tokens.colorNeutralForeground2, fontSize: "18px", lineHeight: "26px" },
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
    fontSize: tokens.fontSizeBase300, fontWeight: tokens.fontWeightSemibold,
  },
  stepDetail: { color: tokens.colorNeutralForeground2, lineHeight: "24px" },
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
  feedback: { lineHeight: "22px", minHeight: "22px" },
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
    fontSize: tokens.fontSizeBase400, lineHeight: "22px",
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
      <Text size={400} weight="semibold">Hier ausprobieren</Text>
      <div className={styles.practiceRow}>
        <span className={styles.practiceIcon} aria-hidden="true">{practiceIcon(exercise.icon, done)}</span>
        <div>
          <Text block size={400} strikethrough={done && exercise.icon === "check"}>{exercise.prompt}</Text>
          {done && exercise.icon === "date" && <Text>{longDate(today)}</Text>}
          {done && exercise.icon === "person" && <Text>Verantwortlich: Alex</Text>}
        </div>
      </div>
      <Button size="large" onClick={() => setDone(!done)}>{done ? "Beispiel zurücksetzen" : exercise.action}</Button>
      <Text className={styles.feedback} role="status">{done ? exercise.result : "Übung mit Beispieldaten."}</Text>
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
      <Field size="large" label="Schnelleingabe ausprobieren" hint="Ersetze „morgen“ durch „Freitag“.">
        <Input size="large" value={input} onChange={(_, data) => setInput(data.value)} />
      </Field>
      <div className={styles.example} aria-live="polite">
        {input.trim() ? (
          <>
            <Text size={400} weight="semibold">{parsed.title}</Text>
            {details.map((detail) => <Text size={400} key={detail}>{detail}</Text>)}
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
            <Text size={400} className={styles.label}>{chapter.label}</Text>
            <h2 className={styles.title} id="guide-chapter" ref={heading} tabIndex={-1}>{chapter.title}</h2>
            <Text className={styles.intro}>{chapter.intro}</Text>
          </header>
          <div className={styles.content} style={chapter.id !== "erfassen" && !chapter.exercise ? { gridTemplateColumns: "minmax(0, 1fr)" } : undefined}>
            <ol className={styles.steps} role="list">
              {chapter.steps.map(([action, detail], i) => (
                <li key={action} className={styles.step}>
                  <span className={styles.marker} aria-hidden="true">{i + 1}</span>
                  <div>
                    <Text block size={400} weight="semibold">{action}</Text>
                    <Text block size={400} className={styles.stepDetail}>{detail}</Text>
                  </div>
                </li>
              ))}
            </ol>
            <div className={styles.side}>
              {chapter.id === "erfassen" && <QuickAddPractice today={me.today} />}
              {chapter.id === "mail" && me.mailInboxAddress && (
                <div className={styles.example}>
                  <Text size={400} weight="semibold">Dein Aufgaben-Postfach</Text>
                  <div className={styles.address}>
                    <Text size={400}>{me.mailInboxAddress}</Text>
                    <Button onClick={() => void copyAddress()}>Adresse kopieren</Button>
                  </div>
                </div>
              )}
              {chapter.exercise && <Practice key={chapter.id} exercise={chapter.exercise} today={me.today} />}
            </div>
          </div>
          <footer className={styles.footer}>
            {previous ? (
              <Button as="a" size="large" href={chapterHref(previous)} icon={<ArrowLeftRegular />}>Zurück</Button>
            ) : <span />}
            <Text aria-label={`Kapitel ${index + 1} von ${chapters.length}`}>{index + 1} / {chapters.length}</Text>
            {next ? (
              <Button as="a" size="large" appearance="primary" href={chapterHref(next)} icon={<ArrowRightRegular />} iconPosition="after">Weiter</Button>
            ) : (
              <Button as="a" size="large" appearance="primary" href={hrefFor({ page: "tasks", view: "mein-tag" })}>Zu „Mein Tag“</Button>
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
