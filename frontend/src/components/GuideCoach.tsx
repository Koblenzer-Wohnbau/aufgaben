import { Button, makeStyles, Text, tokens } from "@fluentui/react-components";
import { ArrowRightRegular, BookOpenRegular, DismissRegular } from "@fluentui/react-icons";
import { GUIDE_ACTIONS, type Chapter } from "../guide";

const useStyles = makeStyles({
  bar: {
    display: "flex", flexWrap: "wrap", alignItems: "center", gap: "10px 16px",
    flexShrink: 0, margin: "12px 32px 0", padding: "12px 14px",
    borderRadius: tokens.borderRadiusLarge, backgroundColor: tokens.colorBrandBackground2,
    "@media (max-width: 760px)": { margin: "8px 12px 0 52px", padding: "10px", gap: "8px" },
  },
  text: { flex: "1 1 260px", display: "grid", gap: "4px", minWidth: 0 },
  label: { display: "flex", alignItems: "center", gap: "8px" },
  controls: { display: "flex", flexWrap: "wrap", gap: "6px", alignItems: "center" },
});

interface Props {
  chapter: Chapter;
  taskId?: number;
  taskOpen: boolean;
  next?: Chapter;
  hint?: string;
  onOpenTask: (id: number) => void;
  onNext: () => void;
  onExit: () => void;
}

export function GuideCoach({ chapter, taskId, taskOpen, next, hint, onOpenTask, onNext, onExit }: Props) {
  const styles = useStyles();
  const taskCreated = chapter.id === "start" && taskId !== undefined;
  const canOpenTask = (chapter.id === "fristen" || chapter.id === "bearbeiten") && taskId !== undefined && !taskOpen;
  return (
    <aside className={styles.bar} aria-label="Rundgang">
      <div className={styles.text}>
        <Text size={400} weight="semibold" className={styles.label}><BookOpenRegular /> {chapter.label}</Text>
        <Text size={400} aria-live="polite">
          {taskCreated ? "Aufgabe gespeichert. Als Nächstes kannst du ihr eine Fälligkeit geben." : hint ?? GUIDE_ACTIONS[chapter.id].hint}
        </Text>
      </div>
      <div className={styles.controls}>
        {canOpenTask && <Button onClick={() => onOpenTask(taskId)}>Aufgabe öffnen</Button>}
        {next && (
          <Button appearance="primary" onClick={onNext} icon={<ArrowRightRegular />} iconPosition="after">
            {taskCreated ? "Fälligkeit setzen" : chapter.id === "start" ? "Überspringen" : "Weiter im Rundgang"}
          </Button>
        )}
        <Button appearance="subtle" icon={<DismissRegular />} onClick={onExit} aria-label="Zur Anleitung zurück">
          {next ? "Beenden" : "Zur Anleitung"}
        </Button>
      </div>
    </aside>
  );
}
