import { Button, makeStyles, Spinner, Text, Tooltip, tokens } from "@fluentui/react-components";
import { AddCircleRegular, CalendarLtrRegular, DismissRegular, NumberSymbolRegular } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { useApp } from "../context";
import { relativeDay } from "../dates";
import type { Task } from "../types";

const useStyles = makeStyles({
  panel: {
    margin: "8px 32px 0",
    padding: "12px 16px",
    borderRadius: "12px",
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorBrandStroke2}`,
    boxShadow: tokens.shadow4,
    animationName: "aufgaben-fade-in",
    animationDuration: "200ms",
  },
  compact: { margin: "8px 16px 0" },
  head: { display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: "4px" },
  group: { marginTop: "10px" },
  groupTitle: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightSemibold },
  row: {
    display: "flex",
    alignItems: "center",
    gap: "10px",
    padding: "6px 4px",
    borderRadius: tokens.borderRadiusMedium,
    ":hover": { backgroundColor: tokens.colorSubtleBackgroundHover },
  },
  title: { flex: 1, minWidth: 0, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
  meta: { display: "flex", gap: "10px", color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  empty: { color: tokens.colorNeutralForeground3, padding: "8px 0" },
});

const GROUPS: { key: "yesterday" | "dueSoon" | "important" | "recent"; label: string }[] = [
  { key: "yesterday", label: "Von gestern" },
  { key: "dueSoon", label: "Bald fällig" },
  { key: "important", label: "Wichtig" },
  { key: "recent", label: "Neu angelegt" },
];

/** Vorschläge für "Mein Tag", ein Klick plant die Aufgabe für heute ein. */
export function SuggestionsPanel({ compact, onClose }: { compact: boolean; onClose: () => void }) {
  const styles = useStyles();
  const { today, notify } = useApp();
  const queryClient = useQueryClient();
  const suggestions = useQuery({ queryKey: ["suggestions"], queryFn: api.suggestions });

  const add = async (task: Task) => {
    // Sofort aus den Vorschlägen nehmen, Server danach.
    queryClient.setQueryData(["suggestions"], (old: typeof suggestions.data) =>
      old
        ? {
            yesterday: old.yesterday.filter((t) => t.id !== task.id),
            dueSoon: old.dueSoon.filter((t) => t.id !== task.id),
            important: old.important.filter((t) => t.id !== task.id),
            recent: old.recent.filter((t) => t.id !== task.id),
          }
        : old,
    );
    try {
      await api.patchTask(task.id, { myDay: true });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["tasks"] }),
        queryClient.invalidateQueries({ queryKey: ["counts"] }),
      ]);
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
      void queryClient.invalidateQueries({ queryKey: ["suggestions"] });
    }
  };

  const data = suggestions.data;
  const total = data ? data.yesterday.length + data.dueSoon.length + data.important.length + data.recent.length : 0;

  return (
    <section className={`${styles.panel} ${compact ? styles.compact : ""}`} aria-label="Vorschläge für Mein Tag">
      <div className={styles.head}>
        <Text weight="semibold">Vorschläge</Text>
        <Button size="small" appearance="subtle" icon={<DismissRegular />} aria-label="Vorschläge schließen" onClick={onClose} />
      </div>
      {suggestions.isLoading && <Spinner size="tiny" />}
      {data && total === 0 && <Text className={styles.empty}>Keine Vorschläge, dein Tag ist gut sortiert.</Text>}
      {data &&
        GROUPS.filter((g) => data[g.key].length > 0).map((g) => (
          <div key={g.key} className={styles.group}>
            <div className={styles.groupTitle}>{g.label}</div>
            {data[g.key].map((t) => (
              <div key={t.id} className={styles.row}>
                <Tooltip content="Zu „Mein Tag“ hinzufügen" relationship="label">
                  <Button appearance="subtle" icon={<AddCircleRegular />} onClick={() => void add(t)} />
                </Tooltip>
                <span className={styles.title}>
                  <Text>{t.title}</Text>
                </span>
                <span className={styles.meta}>
                  {t.dueDate && (
                    <span>
                      <CalendarLtrRegular /> {relativeDay(t.dueDate, today)}
                    </span>
                  )}
                  {t.project && (
                    <span>
                      <NumberSymbolRegular /> {t.project.label}
                    </span>
                  )}
                </span>
              </div>
            ))}
          </div>
        ))}
    </section>
  );
}
