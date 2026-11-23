import { useMemo, useRef, useState } from "react";
import { Avatar, Button, makeStyles, Text, tokens } from "@fluentui/react-components";
import {
  AddRegular,
  ClockAlarmRegular,
  ArrowRepeatAllRegular,
  CalendarLtrRegular,
  ClockRegular,
  ImportantRegular,
  NumberSymbolRegular,
  PersonRegular,
} from "@fluentui/react-icons";
import { useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { initials, matchProject, matchUser, mentionFor, useApp } from "../context";
import { RECURRENCE_LABEL, relativeDay } from "../dates";
import { activeMention, parseQuickAdd } from "../quickadd";
import type { CreateTask } from "../types";

const useStyles = makeStyles({
  wrap: { position: "relative" },
  box: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    padding: "4px 8px 4px 12px",
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow4,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    ":focus-within": { border: `1px solid ${tokens.colorBrandStroke1}`, boxShadow: tokens.shadow8 },
  },
  input: {
    flex: 1,
    border: "none",
    outline: "none",
    background: "transparent",
    fontSize: tokens.fontSizeBase400,
    padding: "10px 0",
    color: tokens.colorNeutralForeground1,
    fontFamily: "inherit",
    minWidth: 0,
  },
  chips: { display: "flex", flexWrap: "wrap", gap: "6px", minHeight: "26px", padding: "8px 4px 0" },
  chip: {
    display: "inline-flex",
    alignItems: "center",
    gap: "4px",
    padding: "2px 10px",
    borderRadius: "999px",
    fontSize: tokens.fontSizeBase200,
    backgroundColor: tokens.colorNeutralBackground5,
    color: tokens.colorNeutralForeground2,
    animationName: "aufgaben-fade-in",
    animationDuration: "160ms",
  },
  brand: { backgroundColor: tokens.colorBrandBackground2, color: tokens.colorBrandForeground2 },
  danger: { backgroundColor: tokens.colorPaletteRedBackground2, color: tokens.colorPaletteRedForeground2 },
  warn: { backgroundColor: tokens.colorPaletteYellowBackground2, color: tokens.colorPaletteYellowForeground2 },
  hint: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200, padding: "2px 4px" },
  suggest: {
    position: "absolute",
    top: "52px",
    left: "8px",
    zIndex: 10,
    width: "min(360px, 90%)",
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow16,
    borderRadius: tokens.borderRadiusMedium,
    padding: "4px",
  },
  option: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    padding: "6px 8px",
    borderRadius: tokens.borderRadiusSmall,
    cursor: "pointer",
  },
  optionActive: { backgroundColor: tokens.colorSubtleBackgroundHover },
});

interface Props {
  defaults: Partial<CreateTask>;
  placeholder?: string;
}

export function QuickAdd({ defaults, placeholder }: Props) {
  const styles = useStyles();
  const { today, projects, users, me, notify } = useApp();
  const queryClient = useQueryClient();
  const [text, setText] = useState("");
  const [caret, setCaret] = useState(0);
  const [busy, setBusy] = useState(false);
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  const todayDate = useMemo(() => {
    const [y, m, d] = today.split("-").map(Number);
    const now = new Date();
    return new Date(y, m - 1, d, now.getHours(), now.getMinutes());
  }, [today]);

  const parsed = useMemo(() => parseQuickAdd(text, todayDate), [text, todayDate]);
  const project = parsed.projectQuery ? matchProject(projects, parsed.projectQuery) : undefined;
  const person = parsed.personQuery ? matchUser(users, parsed.personQuery) : undefined;

  const mention = activeMention(text, caret);
  const suggestions = useMemo(() => {
    if (!mention) return [];
    const q = mention.query.toLowerCase();
    if (mention.kind === "#") {
      return projects
        .filter((p) => !q || p.label.toLowerCase().includes(q))
        .slice(0, 6)
        .map((p) => ({ key: `p${p.id}`, label: p.label, sub: p.teamName ?? "", insert: p.number ?? p.name.split(" ")[0], avatar: null as string | null }));
    }
    return users
      .filter((u) => !q || u.name.toLowerCase().includes(q) || u.email?.toLowerCase().startsWith(q))
      .slice(0, 6)
      .map((u) => ({ key: `u${u.id}`, label: u.name, sub: u.jobTitle ?? "", insert: mentionFor(u, users), avatar: u.name }));
  }, [mention, projects, users]);

  const applySuggestion = (insert: string) => {
    if (!mention) return;
    const before = text.slice(0, mention.start + 1);
    const after = text.slice(caret);
    const next = `${before}${insert} ${after.trimStart()}`;
    setText(next);
    const pos = before.length + insert.length + 1;
    setCaret(pos);
    requestAnimationFrame(() => {
      inputRef.current?.focus();
      inputRef.current?.setSelectionRange(pos, pos);
    });
    setActive(0);
  };

  const submit = async () => {
    if (!text.trim() || busy) return;
    if (parsed.personQuery && !person) {
      notify(`Keine Person zu „@${parsed.personQuery}“ gefunden`, "error");
      return;
    }
    if (parsed.projectQuery && !project) {
      notify(`Kein Projekt zu „#${parsed.projectQuery}“ gefunden`, "error");
      return;
    }
    setBusy(true);
    try {
      const body: CreateTask = {
        ...defaults,
        title: parsed.title,
        dueDate: parsed.dueDate ?? defaults.dueDate ?? null,
        dueTime: parsed.dueTime ?? null,
        priority: parsed.priority ?? defaults.priority,
        kind: parsed.kind ?? defaults.kind,
        recurrence: parsed.recurrence ?? "",
        projectId: project?.id ?? defaults.projectId ?? null,
        assigneeId: person?.id ?? defaults.assigneeId ?? null,
      };
      await api.createTask(body);
      setText("");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["tasks"] }),
        queryClient.invalidateQueries({ queryKey: ["counts"] }),
      ]);
      if (person && person.id !== me.id) notify(`An ${person.name} delegiert, ${person.name.split(" ")[0]} wird benachrichtigt`);
    } catch (e) {
      notify(e instanceof Error ? e.message : "Aufgabe konnte nicht angelegt werden", "error");
    } finally {
      setBusy(false);
      inputRef.current?.focus();
    }
  };

  const chips = [];
  if (parsed.dueDate)
    chips.push(
      <span key="d" className={`${styles.chip} ${styles.brand}`}>
        <CalendarLtrRegular /> {relativeDay(parsed.dueDate, today)}
      </span>,
    );
  if (parsed.dueTime)
    chips.push(
      <span key="t" className={`${styles.chip} ${styles.brand}`}>
        <ClockRegular /> {parsed.dueTime}
      </span>,
    );
  if (parsed.projectQuery)
    chips.push(
      <span key="p" className={`${styles.chip} ${project ? styles.brand : styles.warn}`}>
        <NumberSymbolRegular /> {project ? project.label : `${parsed.projectQuery}?`}
      </span>,
    );
  if (parsed.personQuery)
    chips.push(
      <span key="u" className={`${styles.chip} ${person ? styles.brand : styles.warn}`}>
        <PersonRegular /> {person ? person.name : `${parsed.personQuery}?`}
      </span>,
    );
  if (parsed.priority === "hoch")
    chips.push(
      <span key="h" className={`${styles.chip} ${styles.danger}`}>
        <ImportantRegular /> Hoch
      </span>,
    );
  if (parsed.kind)
    chips.push(
      <span key="k" className={`${styles.chip} ${styles.warn}`}>
        <ClockAlarmRegular /> Wiedervorlage
      </span>,
    );
  if (parsed.recurrence)
    chips.push(
      <span key="r" className={styles.chip}>
        <ArrowRepeatAllRegular /> {RECURRENCE_LABEL[parsed.recurrence]}
      </span>,
    );

  return (
    <div className={styles.wrap}>
      <div className={styles.box}>
        <AddRegular fontSize={20} />
        <input
          ref={inputRef}
          className={styles.input}
          value={text}
          placeholder={placeholder ?? "Aufgabe hinzufügen, z. B. „Montag 10 Uhr Angebot prüfen #1001 @Becker !hoch“"}
          aria-label="Neue Aufgabe"
          onChange={(e) => {
            setText(e.target.value);
            setCaret(e.target.selectionStart ?? e.target.value.length);
            setActive(0);
          }}
          onSelect={(e) => setCaret(e.currentTarget.selectionStart ?? 0)}
          onKeyDown={(e) => {
            if (suggestions.length > 0) {
              if (e.key === "ArrowDown") {
                e.preventDefault();
                setActive((a) => (a + 1) % suggestions.length);
                return;
              }
              if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive((a) => (a - 1 + suggestions.length) % suggestions.length);
                return;
              }
              if (e.key === "Tab" || (e.key === "Enter" && mention && mention.query.length > 0)) {
                e.preventDefault();
                applySuggestion(suggestions[active].insert);
                return;
              }
            }
            if (e.key === "Enter") {
              e.preventDefault();
              void submit();
            }
            if (e.key === "Escape") setText("");
          }}
        />
        <Button appearance="primary" size="small" disabled={!text.trim() || busy} onClick={() => void submit()}>
          Hinzufügen
        </Button>
      </div>
      {suggestions.length > 0 && (
        <div className={styles.suggest} role="listbox">
          {suggestions.map((s, i) => (
            <div
              key={s.key}
              role="option"
              aria-selected={i === active}
              className={`${styles.option} ${i === active ? styles.optionActive : ""}`}
              onMouseDown={(e) => {
                e.preventDefault();
                applySuggestion(s.insert);
              }}
            >
              {s.avatar ? <Avatar size={24} name={s.avatar} initials={initials(s.avatar)} color="colorful" /> : <NumberSymbolRegular />}
              <div>
                <Text block size={300}>
                  {s.label}
                </Text>
                {s.sub && (
                  <Text block size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    {s.sub}
                  </Text>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
      <div className={styles.chips}>
        {chips.length > 0 ? chips : <span className={styles.hint}>Tipp: # für Projekt, @ für Person, ! für wichtig, „wv“ für Wiedervorlage</span>}
      </div>
    </div>
  );
}
