import { useState, type ReactNode } from "react";
import {
  Button,
  Caption1,
  makeStyles,
  mergeClasses,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  tokens,
} from "@fluentui/react-components";
import { ArrowHookUpLeftRegular, BeachRegular, DismissRegular, PeopleSwapRegular } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { eventsByTask, rangeLabel, remainingLabel, summaryGroups, summaryLine, taskCount } from "../absence";
import { api } from "../api";
import { useApp } from "../context";
import { shortDateTime } from "../dates";
import { weekdayDate } from "../later";
import { navigate } from "../route";
import type { Absence, DeputyInfo, HandoverState } from "../types";

const useStyles = makeStyles({
  // Die Liste darunter ist eine scrollende Flex-Spalte, ohne flexShrink: 0 würden Karte und Banner gestaucht.
  card: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "column",
    gap: "12px",
    padding: "14px 16px",
    marginBottom: "6px",
    borderRadius: "12px",
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow4,
    borderLeft: `4px solid ${tokens.colorBrandStroke1}`,
    animationName: "aufgaben-fade-in",
    animationDuration: "300ms",
  },
  head: { display: "flex", alignItems: "flex-start", gap: "10px" },
  headIcon: { fontSize: "24px", color: tokens.colorBrandForeground1, flexShrink: 0, marginTop: "2px" },
  headText: { flex: 1, minWidth: 0, display: "flex", flexDirection: "column" },
  muted: { color: tokens.colorNeutralForeground3 },
  section: { display: "flex", flexDirection: "column", gap: "4px" },
  sectionHead: { display: "flex", alignItems: "center", gap: "8px", flexWrap: "wrap", minHeight: "28px" },
  sectionTitle: { flex: 1, minWidth: "140px" },
  row: {
    display: "flex",
    alignItems: "center",
    flexWrap: "wrap",
    gap: "2px 10px",
    padding: "6px 8px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  rowText: { flex: "1 1 200px", minWidth: 0, display: "flex", flexDirection: "column" },
  title: {
    textAlign: "left",
    border: "none",
    background: "transparent",
    padding: 0,
    fontFamily: "inherit",
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflowWrap: "anywhere",
  },
  link: { cursor: "pointer", ":hover": { textDecorationLine: "underline" } },
  done: { textDecorationLine: "line-through", color: tokens.colorNeutralForeground3 },
  event: { display: "block", paddingLeft: "2px", overflowWrap: "anywhere" },
  banner: { flexShrink: 0, marginBottom: "6px" },
});

/** „Was ist passiert?“ nach dem Urlaub, oben in „Mein Tag“, bis die Person sie schließt. */
export function ReturnSummary({ absence }: { absence: Absence }) {
  const styles = useStyles();
  const { me, notify, openTask } = useApp();
  const queryClient = useQueryClient();
  const summary = useQuery({ queryKey: ["absences", "summary", absence.id], queryFn: () => api.absenceSummary(absence.id) });
  const [busy, setBusy] = useState(false);
  const [allEvents, setAllEvents] = useState(false);

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["absences"] }),
      queryClient.invalidateQueries({ queryKey: ["tasks"] }),
      queryClient.invalidateQueries({ queryKey: ["counts"] }),
      queryClient.invalidateQueries({ queryKey: ["task"] }),
    ]);

  const takeBack = async (taskIds?: number[]) => {
    setBusy(true);
    try {
      const { count } = await api.takeBack(absence.id, taskIds);
      notify(count === 0 ? "Nichts mehr zurückzuholen" : `${taskCount(count)} zurück bei dir, steht in „Mein Tag“`, count === 0 ? "info" : "success");
      await refresh();
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    } finally {
      setBusy(false);
    }
  };

  const close = async () => {
    try {
      await api.closeAbsenceSummary(absence.id);
      await queryClient.invalidateQueries({ queryKey: ["absences"] });
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  const data = summary.data;
  const groups = summaryGroups(data?.handovers ?? [], me.id);
  const byTask = eventsByTask(data?.events ?? []);
  const shownEvents = allEvents ? byTask : byTask.slice(0, 5);
  const nothing = !!data && data.handovers.length === 0 && data.events.length === 0 && data.kept === 0;
  const deputy = absence.deputy.name;

  const title = (h: { taskId: number; title: string; canOpen: boolean }, done = false) =>
    h.canOpen ? (
      <button type="button" className={mergeClasses(styles.title, styles.link, done && styles.done)} onClick={() => openTask(h.taskId)}>
        {h.title}
      </button>
    ) : (
      <span className={mergeClasses(styles.title, done && styles.done)}>{h.title}</span>
    );

  const section = (label: string, items: HandoverState[], render: (h: HandoverState) => ReactNode, action?: ReactNode) =>
    items.length > 0 && (
      <div className={styles.section}>
        <div className={styles.sectionHead}>
          <Text weight="semibold" className={styles.sectionTitle}>
            {label} ({items.length})
          </Text>
          {action}
        </div>
        {items.map((h) => (
          <div key={h.taskId} className={styles.row}>
            {render(h)}
          </div>
        ))}
      </div>
    );

  return (
    <section className={styles.card} aria-label="Was ist passiert?">
      <div className={styles.head}>
        <BeachRegular className={styles.headIcon} />
        <div className={styles.headText}>
          <Text weight="semibold" size={400}>
            Willkommen zurück! Was ist passiert?
          </Text>
          <Caption1 className={styles.muted}>
            Urlaub {rangeLabel(absence.from, absence.until)} · Vertretung: {deputy}
          </Caption1>
        </div>
        <Button appearance="subtle" icon={<DismissRegular />} aria-label="Übersicht schließen" title="Übersicht schließen" onClick={() => void close()} />
      </div>

      {summary.isLoading && <Spinner size="small" />}
      {nothing && <Text className={styles.muted}>Während deines Urlaubs ist an deinen Aufgaben nichts passiert.</Text>}

      {section(
        `Noch bei ${deputy}`,
        groups.open,
        (h) => (
          <>
            <div className={styles.rowText}>{title(h)}</div>
            <Button size="small" icon={<ArrowHookUpLeftRegular />} disabled={busy} onClick={() => void takeBack([h.taskId])}>
              Zurück an mich
            </Button>
          </>
        ),
        groups.open.length > 1 && (
          <Button size="small" appearance="primary" icon={<ArrowHookUpLeftRegular />} disabled={busy} onClick={() => void takeBack()}>
            Alle zurück an mich
          </Button>
        ),
      )}

      {section("Erledigt", groups.done, (h) => (
        <div className={styles.rowText}>
          {title(h, true)}
          <Caption1 className={styles.muted}>
            von {h.completedBy?.name ?? "jemandem"}
            {h.completedAt ? ` am ${shortDateTime(h.completedAt)}` : ""}
          </Caption1>
        </div>
      ))}

      {section("Wieder bei dir", groups.back, (h) => (
        <div className={styles.rowText}>{title(h)}</div>
      ))}

      {section("Weitergegeben", groups.elsewhere, (h) => (
        <div className={styles.rowText}>
          {title(h)}
          <Caption1 className={styles.muted}>liegt jetzt bei {h.assignee.name}</Caption1>
        </div>
      ))}

      {data && data.kept > 0 && (
        <Caption1 className={styles.muted}>
          {taskCount(data.kept)} hattest du behalten, {data.kept === 1 ? "sie ist" : "sie sind"} wieder in deinen Listen.
        </Caption1>
      )}

      {byTask.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionHead}>
            <Text weight="semibold" className={styles.sectionTitle}>
              Neu an deinen Aufgaben ({byTask.length})
            </Text>
          </div>
          {shownEvents.map((g) => {
            const canOpen = !groups.open.concat(groups.elsewhere).some((h) => h.taskId === g.taskId && !h.canOpen);
            return (
              <div key={g.taskId} className={styles.row}>
                <div className={styles.rowText}>
                  {title({ taskId: g.taskId, title: g.title, canOpen })}
                  {g.events.slice(0, 4).map((e) => (
                    <Caption1 key={e.id} className={mergeClasses(styles.event, styles.muted)}>
                      <b>{e.actor?.name ?? "System"}</b> {summaryLine(e)} · {shortDateTime(e.createdAt)}
                    </Caption1>
                  ))}
                  {g.events.length > 4 && <Caption1 className={styles.muted}>… und {g.events.length - 4} weitere</Caption1>}
                </div>
              </div>
            );
          })}
          {byTask.length > shownEvents.length && (
            <Button appearance="subtle" size="small" onClick={() => setAllEvents(true)} style={{ alignSelf: "flex-start" }}>
              Alle {byTask.length} anzeigen
            </Button>
          )}
        </div>
      )}
    </section>
  );
}

/** Banner für die Vertretung während des Urlaubs, mit Link auf die übergebenen Aufgaben. */
export function DeputyBanner({ deputy }: { deputy: DeputyInfo }) {
  const styles = useStyles();
  const { today } = useApp();
  return (
    <MessageBar intent="info" icon={<PeopleSwapRegular />} className={styles.banner} layout="multiline">
      <MessageBarBody>
        <MessageBarTitle>Vertretung für {deputy.person.name}</MessageBarTitle>
        bis {weekdayDate(deputy.until)} ({remainingLabel(deputy.until, today)})
        {deputy.open > 0 ? ` · ${taskCount(deputy.open)} übergeben und noch offen` : " · alles Übergebene ist erledigt"}
      </MessageBarBody>
      <MessageBarActions>
        <Button size="small" onClick={() => navigate({ page: "tasks", view: "vertretung", id: deputy.absenceId })}>
          Übergebene Aufgaben
        </Button>
      </MessageBarActions>
    </MessageBar>
  );
}
