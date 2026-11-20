import { useState, type MouseEvent } from "react";
import { Avatar, makeStyles, mergeClasses, Text, Tooltip, tokens } from "@fluentui/react-components";
import {
  ClockAlarmRegular,
  ArrowRepeatAllRegular,
  AttachRegular,
  MailRegular,
  CalendarLtrRegular,
  CheckmarkCircleFilled,
  CheckmarkCircleRegular,
  CircleRegular,
  NumberSymbolRegular,
  StarFilled,
  StarRegular,
  TaskListLtrRegular,
  WeatherSunnyRegular,
} from "@fluentui/react-icons";
import { useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { initials, useApp } from "../context";
import { relativeDay } from "../dates";
import type { Task } from "../types";

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
    ":hover": { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  selected: { outline: `2px solid ${tokens.colorBrandStroke1}` },
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
}

export function TaskRow({ task, selected, showAssignee }: Props) {
  const styles = useStyles();
  const { today, me, openTask, notify } = useApp();
  const queryClient = useQueryClient();
  const [hover, setHover] = useState(false);
  const [optimisticDone, setOptimisticDone] = useState<boolean | null>(null);
  const done = optimisticDone ?? task.status === "erledigt";

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
        if (task.isDelegated && task.createdBy.id !== me.id) notify(`Erledigt – ${task.createdBy.name.split(" ")[0]} wird benachrichtigt`);
        else if (task.recurrence) notify("Erledigt – nächster Termin ist angelegt");
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
    try {
      await api.patchTask(task.id, { priority: task.priority === "hoch" ? "normal" : "hoch" });
      await refresh();
    } catch (err) {
      notify(err instanceof Error ? err.message : "Fehler", "error");
    }
  };

  const checkDone = task.checklist.filter((c) => c.done).length;
  const dueLabel = task.dueDate ? relativeDay(task.dueDate, today) + (task.dueTime ? `, ${task.dueTime}` : "") : null;
  const delegatedToOther = task.isDelegated && task.assignee.id !== me.id;
  const fromOther = task.isDelegated && task.createdBy.id !== me.id;

  return (
    <div
      className={mergeClasses(styles.row, selected && styles.selected)}
      onClick={() => openTask(task.id)}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => e.key === "Enter" && openTask(task.id)}
    >
      <button
        className={mergeClasses(styles.check, done && styles.checkDone)}
        onClick={toggle}
        onMouseEnter={() => setHover(true)}
        onMouseLeave={() => setHover(false)}
        aria-label={done ? "Als offen markieren" : "Als erledigt markieren"}
      >
        {done ? <CheckmarkCircleFilled /> : hover ? <CheckmarkCircleRegular /> : <CircleRegular />}
      </button>
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
      <button className={mergeClasses(styles.star, task.priority === "hoch" && styles.starOn)} onClick={toggleStar} aria-label="Wichtig umschalten">
        {task.priority === "hoch" ? <StarFilled /> : <StarRegular />}
      </button>
    </div>
  );
}
