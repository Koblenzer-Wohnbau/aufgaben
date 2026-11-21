import { useEffect, useMemo, useState } from "react";
import { Button, Caption1, Checkbox, makeStyles, mergeClasses, MessageBar, MessageBarBody, Spinner, Text, Title2, tokens } from "@fluentui/react-components";
import { ArrowUndoRegular, DeleteDismissRegular, DeleteRegular, NumberSymbolRegular } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { useApp } from "../context";
import { shortDateTime } from "../dates";
import { allState, bulkMessage, tasksLabel, toggleSelection, TRASH_DAYS, trashDaysLeft } from "../selection";
import type { BulkAction, Task } from "../types";
import { EmptyState } from "./EmptyState";

const useStyles = makeStyles({
  view: { flex: 1, display: "flex", flexDirection: "column", overflow: "hidden" },
  header: {
    padding: "20px 32px 8px",
    display: "flex",
    alignItems: "flex-end",
    gap: "12px",
    flexWrap: "wrap",
    "@media (max-width: 760px)": { padding: "14px 16px 8px 56px" },
  },
  compactHeader: { padding: "12px 16px 4px" },
  headText: { flex: 1, minWidth: "200px", display: "flex", flexDirection: "column", gap: "4px" },
  titleRow: { display: "flex", alignItems: "center", gap: "12px" },
  titleIcon: { fontSize: "28px", color: tokens.colorBrandForeground1, display: "flex" },
  muted: { color: tokens.colorNeutralForeground3 },
  bar: {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    gap: "8px 12px",
    margin: "8px 32px 4px",
    padding: "6px 12px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow4,
    "@media (max-width: 760px)": { margin: "8px 16px 4px" },
  },
  count: { flex: 1, minWidth: "90px", color: tokens.colorNeutralForeground3 },
  list: {
    flex: 1,
    overflowY: "auto",
    padding: "8px 32px 48px",
    display: "flex",
    flexDirection: "column",
    gap: "6px",
    "@media (max-width: 760px)": { padding: "8px 16px 32px" },
  },
  row: {
    display: "flex",
    alignItems: "center",
    gap: "10px",
    padding: "8px 12px",
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    cursor: "pointer",
  },
  checked: { backgroundColor: tokens.colorBrandBackground2 },
  body: { flex: 1, minWidth: 0 },
  title: { display: "block", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", color: tokens.colorNeutralForeground2 },
  meta: { display: "flex", flexWrap: "wrap", gap: "2px 12px", color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  soon: { color: tokens.colorPaletteRedForeground1 },
  actions: { display: "flex", gap: "4px", flexShrink: 0 },
});

/** Papierkorb: gelöschte Aufgaben wiederherstellen oder endgültig löschen. Nach 30 Tagen leert der Server ihn selbst. */
export function TrashView({ compact }: { compact: boolean }) {
  const styles = useStyles();
  const { me, notify } = useApp();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ["tasks", "papierkorb"], queryFn: () => api.tasks("papierkorb") });
  const [picked, setPicked] = useState<Set<number>>(() => new Set());
  const [anchor, setAnchor] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);

  const tasks = useMemo(() => query.data ?? [], [query.data]);
  const ids = useMemo(() => tasks.map((t) => t.id), [tasks]);
  const chosen = tasks.filter((t) => picked.has(t.id));

  // Weggefallene Einträge (z. B. in einem anderen Fenster wiederhergestellt) aus der Auswahl nehmen
  useEffect(() => setPicked((p) => new Set([...p].filter((id) => ids.includes(id)))), [ids]);

  const refresh = () =>
    Promise.all([queryClient.invalidateQueries({ queryKey: ["tasks"] }), queryClient.invalidateQueries({ queryKey: ["counts"] })]);

  const run = async (fn: () => Promise<string>) => {
    setBusy(true);
    try {
      notify(await fn());
      setPicked(new Set());
      await refresh();
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    } finally {
      setBusy(false);
    }
  };

  const bulk = (action: BulkAction, list: Task[]) => run(async () => bulkMessage(action, await api.bulkTasks(action, list.map((t) => t.id))));

  const restore = (t: Task) =>
    run(async () => {
      await api.restoreTask(t.id);
      return `„${t.title}“ wiederhergestellt`;
    });

  const purge = (list: Task[]) => {
    if (list.length === 0) return;
    const what = list.length === 1 ? `„${list[0].title}“` : tasksLabel(list.length);
    if (!window.confirm(`${what} endgültig löschen? Das lässt sich nicht rückgängig machen.`)) return;
    void bulk("endgueltig-loeschen", list);
  };

  const empty = () => {
    if (!window.confirm(`Alle ${tasksLabel(tasks.length)} im Papierkorb endgültig löschen? Das lässt sich nicht rückgängig machen.`)) return;
    void run(async () => `${tasksLabel((await api.emptyTrash()).done)} endgültig gelöscht`);
  };

  const select = (id: number, range: boolean) => {
    setPicked((p) => toggleSelection(p, ids, id, anchor, range));
    setAnchor(id);
  };

  return (
    <div className={styles.view}>
      <header className={mergeClasses(styles.header, compact && styles.compactHeader)}>
        <div className={styles.headText}>
          <div className={styles.titleRow}>
            <span className={styles.titleIcon}>
              <DeleteRegular />
            </span>
            <Title2 as="h1">Papierkorb</Title2>
          </div>
          <Text className={styles.muted}>
            Gelöschte Aufgaben bleiben {TRASH_DAYS} Tage hier und werden danach endgültig gelöscht · {tasksLabel(tasks.length)}
          </Text>
        </div>
        {tasks.length > 0 && (
          <Button icon={<DeleteDismissRegular />} disabled={busy} onClick={empty}>
            Papierkorb leeren
          </Button>
        )}
      </header>

      {tasks.length > 0 && (
        <div className={styles.bar} role="toolbar" aria-label="Ausgewählte Aufgaben im Papierkorb">
          <Checkbox
            shape="circular"
            checked={allState(picked, ids)}
            onChange={(_, d) => setPicked(d.checked === true ? new Set(ids) : new Set())}
            label={`Alle (${ids.length})`}
          />
          <Text className={styles.count}>{chosen.length === 0 ? "Nichts ausgewählt" : `${chosen.length} ausgewählt`}</Text>
          <Button icon={<ArrowUndoRegular />} disabled={busy || chosen.length === 0} onClick={() => void bulk("wiederherstellen", chosen)}>
            Wiederherstellen
          </Button>
          <Button icon={<DeleteDismissRegular />} disabled={busy || chosen.length === 0} onClick={() => purge(chosen)}>
            Endgültig löschen
          </Button>
        </div>
      )}

      <div className={styles.list}>
        {query.isLoading && <Spinner style={{ marginTop: 32 }} />}
        {query.error && (
          <MessageBar intent="error">
            <MessageBarBody>{query.error instanceof Error ? query.error.message : "Fehler beim Laden"}</MessageBarBody>
          </MessageBar>
        )}
        {query.data && tasks.length === 0 && <EmptyState text="Der Papierkorb ist leer." />}
        {tasks.map((t) => {
          const left = t.deletedAt ? trashDaysLeft(t.deletedAt) : TRASH_DAYS;
          const checked = picked.has(t.id);
          return (
            <div
              key={t.id}
              className={mergeClasses(styles.row, checked && styles.checked)}
              role="button"
              tabIndex={0}
              aria-pressed={checked}
              onClick={(e) => select(t.id, e.shiftKey)}
              onKeyDown={(e) => {
                if (e.target === e.currentTarget && (e.key === " " || e.key === "Enter")) {
                  e.preventDefault();
                  select(t.id, e.shiftKey);
                }
              }}
            >
              <Checkbox shape="circular" checked={checked} tabIndex={-1} aria-label={`„${t.title}“ auswählen`} />
              <div className={styles.body}>
                <Text className={styles.title}>{t.title}</Text>
                <div className={styles.meta}>
                  <span>
                    Gelöscht {t.deletedAt ? shortDateTime(t.deletedAt) : ""}
                    {t.deletedBy && t.deletedBy.id !== me.id ? ` von ${t.deletedBy.name}` : ""}
                  </span>
                  <span className={left <= 3 ? styles.soon : undefined}>{left === 1 ? "noch 1 Tag" : `noch ${left} Tage`}</span>
                  {t.status === "erledigt" && <span>erledigt</span>}
                  {t.project && (
                    <span>
                      <NumberSymbolRegular /> {t.project.label}
                    </span>
                  )}
                </div>
              </div>
              <div className={styles.actions} onClick={(e) => e.stopPropagation()}>
                <Button size="small" icon={<ArrowUndoRegular />} disabled={busy} onClick={() => void restore(t)}>
                  Wiederherstellen
                </Button>
                <Button size="small" appearance="subtle" icon={<DeleteDismissRegular />} disabled={busy} aria-label="Endgültig löschen" title="Endgültig löschen" onClick={() => purge([t])} />
              </div>
            </div>
          );
        })}
        {tasks.length > 0 && (
          <Caption1 className={styles.muted} style={{ marginTop: 8 }}>
            Wiederherstellen kann, wer die Aufgabe angelegt oder gelöscht hat. Umschalt+Klick wählt einen Bereich aus.
          </Caption1>
        )}
      </div>
    </div>
  );
}
