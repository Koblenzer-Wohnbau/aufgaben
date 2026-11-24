import { useEffect, useRef } from "react";
import { Button, makeStyles, mergeClasses, Text, tokens } from "@fluentui/react-components";
import { ArrowLeftRegular, ArrowRightRegular, BookOpenRegular, PlayRegular } from "@fluentui/react-icons";
import { useApp } from "../context";
import { chaptersFor, GUIDE_ACTIONS, type Chapter, type GuideTopic } from "../guide";
import { hrefFor } from "../route";

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
  banner: { display: "grid", gap: "12px", padding: "clamp(18px, 4vw, 34px)", backgroundColor: tokens.colorBrandBackground2 },
  title: {
    margin: 0, maxWidth: "24ch",
    fontSize: "clamp(30px, 4vw, 44px)", lineHeight: 1.12, fontWeight: tokens.fontWeightSemibold,
    overflowWrap: "anywhere",
  },
  intro: { maxWidth: "52ch", color: tokens.colorNeutralForeground2, fontSize: "18px", lineHeight: "26px" },
  content: { display: "grid", justifyItems: "start", gap: "20px", padding: "clamp(16px, 4vw, 30px)" },
  steps: { listStyleType: "none", margin: 0, padding: 0, display: "grid", gap: "12px" },
  step: { display: "grid", gridTemplateColumns: "26px minmax(0, 1fr)", gap: "10px", alignItems: "start" },
  marker: {
    display: "grid", placeItems: "center", width: "24px", height: "24px",
    borderRadius: "50%", backgroundColor: tokens.colorBrandBackground2,
    fontSize: tokens.fontSizeBase300, fontWeight: tokens.fontWeightSemibold,
  },
  stepDetail: { color: tokens.colorNeutralForeground2, lineHeight: "24px" },
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

const chapterHref = (chapter: Chapter) => hrefFor({ page: "anleitung", topic: chapter.id });

export function GuideView({ topic, onTry }: { topic?: string; onTry: (topic: GuideTopic) => void }) {
  const styles = useStyles();
  const { me } = useApp();
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

  return (
    <div className={styles.view} ref={view}>
      <h1 className={styles.heading}><BookOpenRegular className={styles.headingIcon} /> Anleitung</h1>
      <div className={styles.layout}>
        <article className={styles.slide} aria-labelledby="guide-chapter">
          <header className={styles.banner}>
            <Text size={400} weight="semibold">{chapter.label}</Text>
            <h2 className={styles.title} id="guide-chapter" ref={heading} tabIndex={-1}>{chapter.title}</h2>
            <Text className={styles.intro}>{chapter.intro}</Text>
          </header>
          <div className={styles.content}>
            <Button appearance="primary" size="large" icon={<PlayRegular />} onClick={() => onTry(chapter.id)}>
              {GUIDE_ACTIONS[chapter.id].label}
            </Button>
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
          </div>
          <footer className={styles.footer}>
            {previous ? (
              <Button as="a" size="large" href={chapterHref(previous)} icon={<ArrowLeftRegular />}>Zurück</Button>
            ) : <span />}
            <Text aria-label={`Kapitel ${index + 1} von ${chapters.length}`}>{index + 1} / {chapters.length}</Text>
            {next ? (
              <Button as="a" size="large" href={chapterHref(next)} icon={<ArrowRightRegular />} iconPosition="after">Nächstes Kapitel</Button>
            ) : <span />}
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
