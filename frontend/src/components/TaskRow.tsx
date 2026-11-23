import { useState, type MouseEvent } from "react";
import { Avatar, Checkbox, makeStyles, mergeClasses, Text, Tooltip, tokens } from "@fluentui/react-components";
import {
  ClockAlarmRegular,
  ArrowRepeatAllRegular,
  AttachRegular,
  MailRegular,
  CalendarLtrRegular,
  CheckmarkCircleFilled,
  CheckmarkCircleRegular,
  CircleRegular,
  HourglassHalfRegular,
  NumberSymbolRegular,
  SnoozeRegular,
  StarFilled,
  StarRegular,
  TaskListLtrRegular,
  WeatherSunnyRegular,
} from "@fluentui/react-icons";
import { useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { initials, useApp } from "../context";
import { relativeDay } from "../dates";
import { startTaskDrag } from "../dnd";
import { followUpChoices, followUpDue, laterAction, sinceLabel, weekdayDate } from "../later";
import type { Task } from "../types";
import { LaterMenu } from "./LaterMenu";

const useStyles = makeStyles({
  row: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    padding: "10px 12px",
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    cursor: "pointer",
    animationName: "aufgaben-fade-in",
    animationDuration: "180ms",
    transitionProperty: "box-shadow, transform, opacity",
    transitionDuration: tokens.durationFaster,
    ":hover": { backgroundColor: tokens.colorNeutralBackground1Hover, boxShadow: tokens.shadow4, transform: "translateY(-1px)" },
    // „Später“ erscheint beim Darüberfahren oder mit der Tastatur
    "&:hover .aufgaben-later, &:focus-within .aufgaben-later": { opacity: 1 },
  },
  later: {
    opacity: 0,
    transitionProperty: "opacity",
    transitionDuration: tokens.durationFaster,
    ":hover": { color: tokens.colorBrandForeground1 },
    // Touch-Geräte kennen kein Darüberfahren
    "@media (hover: none)": { opacity: 1 },
  },
  dragging: { opacity: 0.45, transform: "scale(0.98)" },
  selected: { outline: `2px solid ${tokens.colorBrandStroke1}` },
  checked: { backgroundColor: tokens.colorBrandBackground2, ":hover": { backgroundColor: tokens.colorBrandBackground2Hover } },
  pick: { display: "flex", marginLeft: "-6px", marginRight: "-6px" },
  check: {
    border: "none",
    background: "transparent",
    padding: 0,
    display: "flex",
    cursor: "pointer",
    fontSize: "22px",
    color: tokens.colorNeutralForeground3,
    ":hover": { color: tokens.colorBrandForeground1 },
  },
  checkDone: { color: tokens.colorPaletteGreenForeground1, animationName: "aufgaben-pop", animationDuration: "260ms" },
  body: { flex: 1, minWidth: 0 },
  title: { display: "block", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
  done: { textDecorationLine: "line-through", color: tokens.colorNeutralForeground3 },
  meta: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "4px 12px", marginTop: "2px", color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  metaItem: { display: "inline-flex", alignItems: "center", gap: "4px" },
  overdue: { color: tokens.colorPaletteRedForeground1, fontWeight: tokens.fontWeightSemibold },
  today: { color: tokens.colorBrandForeground1 },
  project: { color: tokens.colorBrandForeground2 },
  followUp: { color: tokens.colorPaletteMarigoldForeground1 },
  waiting: { color: tokens.colorBrandForeground2 },
  waitingDue: { color: tokens.colorPaletteDarkOrangeForeground1, fontWeight: tokens.fontWeightSemibold },
  star: {
    border: "none",
    background: "transparent",
    padding: "4px",
    display: "flex",
    cursor: "pointer",
    fontSize: "20px",
    color: tokens.colorNeutralForeground3,
  },
  starOn: { color: tokens.colorPaletteMarigoldForeground1 },
  status: { padding: "1px 8px", borderRadius: "999px", fontSize: tokens.fontSizeBase100 },
});

interface Props {
  task: Task;
  selected: boolean;
  showAssignee: boolean;
  /** Auswahlmodus: links ein runder Haken statt „erledigen“, ein Klick wählt aus statt zu öffnen. */
  selecting?: boolean;
  checked?: boolean;
  /** range: mit Umschalt geklickt, alles seit dem letzten Klick auswählen */
  onSelect?: (id: number, range: boolean) => void;
}

export function TaskRow({ task, selected, showAssignee, selecting = false, checked = false, onSelect }: Props) {
  const styles = useStyles();
  const { today, me, openTask, notify } = useApp();
  const queryClient = useQueryClient();
  const [hover, setHover] = useState(false);
  const [optimisticDone, setOptimisticDone] = useState<boolean | null>(null);
  const [optimisticHigh, setOptimisticHigh] = useState<boolean | null>(null);
  const [dragging, setDragging] = useState(false);
  const done = optimisticDone ?? task.status === "erledigt";
  const high = optimisticHigh ?? task.priority === "hoch";

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["tasks"] }),
      queryClient.invalidateQueries({ queryKey: ["counts"] }),
      queryClient.invalidateQueries({ queryKey: ["task", task.id] }),
    ]);

  const toggle = async (e: MouseEvent) => {
    e.stopPropagation();
    const next = !done;
    setOptimisticDone(next);
    try {
      if (next) {
        await api.completeTask(task.id);
        if (task.isDelegated && task.createdBy.id !== me.id) notify(`Erledigt, ${task.createdBy.name.split(" ")[0]} wird benachrichtigt`);
        else if (task.recurrence) notify("Erledigt, nächster Termin ist angelegt");
      } else {
        await api.reopenTask(task.id);
      }
      await refresh();
    } catch (err) {
      notify(err instanceof Error ? err.message : "Fehler", "error");
    } finally {
      setOptimisticDone(null);
    }
  };

  const toggleStar = async (e: MouseEvent) => {
    e.stopPropagation();
    const next = !high;
    setOptimisticHigh(next);
    try {
      await api.patchTask(task.id, { priority: next ? "hoch" : "normal" });
      await refresh();
    } catch (err) {
      notify(err instanceof Error ? err.message : "Fehler", "error");
    } finally {
      setOptimisticHigh(null);
    }
  };

  const later = async (date: string) => {
    const action = laterAction(task, date, today);
    try {
      await api.patchTask(task.id, action.patch);
      notify(action.message);
      await refresh();
    } catch (err) {
      notify(err instanceof Error ? err.message : "Fehler", "error");
    }
  };

  const checkDone = task.checklist.filter((c) => c.done).length;
  const dueLabel = task.dueDate ? relativeDay(task.dueDate, today) + (task.dueTime ? `, ${task.dueTime}` : "") : null;
  const delegatedToOther = task.isDelegated && task.assignee.id !== me.id;
  const fromOther = task.isDelegated && task.createdBy.id !== me.id;
  const waitingDue = followUpDue(task, today);
  // „Später“ plant die verantwortliche Person selbst
  const canPlan = !done && !selecting && task.assignee.id === me.id;

  return (
    <div
      className={mergeClasses(styles.row, selected && !selecting && styles.selected, dragging && styles.dragging, selecting && checked && styles.checked)}
      onClick={(e) => (selecting ? onSelect?.(task.id, e.shiftKey) : openTask(task.id))}
      draggable={!done && !selecting}
      onDragStart={(e) => {
        startTaskDrag(e, task);
        setDragging(true);
      }}
      onDragEnd={() => setDragging(false)}
      title={done || selecting ? undefined : "Zum Verschieben auf einen Eintrag links ziehen"}
      role="button"
      aria-pressed={selecting ? checked : undefined}
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.target !== e.currentTarget) return;
        if (selecting && (e.key === " " || e.key === "Enter")) {
          e.preventDefault();
          onSelect?.(task.id, e.shiftKey);
        } else if (e.key === "Enter") {
          openTask(task.id);
        }
      }}
    >
      {selecting ? (
        <span className={styles.pick}>
          {/* Der Klick läuft zur Zeile hoch und wählt dort aus, so wirkt auch Umschalt+Klick */}
          <Checkbox shape="circular" checked={checked} tabIndex={-1} aria-label={`„${task.title}“ auswählen`} />
        </span>
      ) : (
        <button
          className={mergeClasses(styles.check, done && styles.checkDone)}
          onClick={toggle}
          onMouseEnter={() => setHover(true)}
          onMouseLeave={() => setHover(false)}
          aria-label={done ? "Als offen markieren" : "Als erledigt markieren"}
        >
          {done ? <CheckmarkCircleFilled /> : hover ? <CheckmarkCircleRegular /> : <CircleRegular />}
        </button>
      )}
      <div className={styles.body}>
        <Text className={mergeClasses(styles.title, done && styles.done)}>{task.title}</Text>
        <div className={styles.meta}>
          {task.myDay && !done && (
            <span className={styles.metaItem}>
              <WeatherSunnyRegular /> Mein Tag
            </span>
          )}
          {dueLabel && (
            <span className={mergeClasses(styles.metaItem, task.isOverdue && !done && styles.overdue, dueLabel.startsWith("heute") && !task.isOverdue && styles.today)}>
              <CalendarLtrRegular /> {dueLabel}
            </span>
          )}
          {task.recurrence && <ArrowRepeatAllRegular aria-label="Wiederholt sich" />}
          {task.waitingFor && !done && (
            <span
              className={mergeClasses(styles.metaItem, waitingDue ? styles.waitingDue : styles.waiting)}
              title={task.waitingSince ? `Wartet ${sinceLabel(task.waitingSince, today)}` : undefined}
            >
              <HourglassHalfRegular />
              {waitingDue
                ? `Nachhaken: ${task.waitingFor}${task.waitingSince ? ` (${sinceLabel(task.waitingSince, today)})` : ""}`
                : `Wartet auf ${task.waitingFor}${task.snoozedUntil ? ` · nachhaken ${weekdayDate(task.snoozedUntil)}` : ""}`}
            </span>
          )}
          {!task.waitingFor && task.isSnoozed && task.snoozedUntil && !done && (
            <span className={styles.metaItem}>
              <SnoozeRegular /> zurückgestellt bis {weekdayDate(task.snoozedUntil)}
            </span>
          )}
          {task.kind === "wiedervorlage" && (
            <span className={mergeClasses(styles.metaItem, styles.followUp)}>
              <ClockAlarmRegular /> Wiedervorlage
            </span>
          )}
          {task.project && (
            <span className={mergeClasses(styles.metaItem, styles.project)}>
              <NumberSymbolRegular /> {task.project.label}
            </span>
          )}
          {task.source === "mail" && (
            <span className={styles.metaItem} title="Per E-Mail angelegt">
              <MailRegular />
            </span>
          )}
          {task.attachments.length > 0 && (
            <span className={styles.metaItem} title="Anhänge">
              <AttachRegular /> {task.attachments.length}
            </span>
          )}
          {task.checklist.length > 0 && (
            <span className={styles.metaItem}>
              <TaskListLtrRegular /> {checkDone}/{task.checklist.length}
            </span>
          )}
          {fromOther && <span className={styles.metaItem}>von {task.createdBy.name}</span>}
          {delegatedToOther && done && task.completedBy && <span className={styles.metaItem}>erledigt von {task.completedBy.name}</span>}
        </div>
      </div>
      {(showAssignee || delegatedToOther) && (
        <Tooltip content={`Verantwortlich: ${task.assignee.name}`} relationship="label">
          <Avatar size={28} name={task.assignee.name} initials={initials(task.assignee.name)} color="colorful" />
        </Tooltip>
      )}
      {canPlan && (
        <LaterMenu
          choices={task.waitingFor ? followUpChoices(today) : undefined}
          title={task.waitingFor ? "Wann nachhaken?" : undefined}
          trigger={
            <button
              className={mergeClasses(styles.star, styles.later, "aufgaben-later")}
              aria-label={task.waitingFor ? "Später nachhaken" : "Später"}
              title={task.waitingFor ? "Später nachhaken" : "Später"}
            >
              <SnoozeRegular />
            </button>
          }
          onPick={(date) => void later(date)}
        />
      )}
      {!selecting && (
        <button className={mergeClasses(styles.star, high && styles.starOn)} onClick={toggleStar} aria-label="Wichtig umschalten">
          {high ? <StarFilled /> : <StarRegular />}
        </button>
      )}
    </div>
  );
}
