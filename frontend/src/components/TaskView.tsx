import { useMemo, useState } from "react";
import {
  Button,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  Title2,
  ToggleButton,
  tokens,
} from "@fluentui/react-components";
import {
  ClockAlarmRegular,
  CalendarLtrRegular,
  CheckmarkCircleRegular,
  ChevronDownRegular,
  ChevronRightRegular,
  ListRegular,
  NumberSymbolRegular,
  PeopleTeamRegular,
  PersonArrowLeftRegular,
  SearchRegular,
  SendRegular,
  StarRegular,
  WeatherSunnyRegular,
} from "@fluentui/react-icons";
import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { api } from "../api";
import { useApp } from "../context";
import { dueGroup, GROUP_LABEL, longDate, type DueGroup } from "../dates";
import type { Route } from "../route";
import type { CreateTask, Task } from "../types";
import { QuickAdd } from "./QuickAdd";
import { TaskRow } from "./TaskRow";

const useStyles = makeStyles({
  view: { flex: 1, display: "flex", flexDirection: "column", overflow: "hidden" },
  header: { padding: "20px 32px 8px", display: "flex", flexDirection: "column", gap: "4px" },
  compactHeader: { padding: "12px 16px 4px" },
  titleRow: { display: "flex", alignItems: "center", gap: "12px", paddingLeft: "0" },
  titleIcon: { fontSize: "28px", color: tokens.colorBrandForeground1 },
  sub: { color: tokens.colorNeutralForeground3 },
  add: { padding: "8px 32px 4px" },
  compactAdd: { padding: "8px 16px 4px" },
  list: { flex: 1, overflowY: "auto", padding: "8px 32px 48px", display: "flex", flexDirection: "column", gap: "6px" },
  compactList: { padding: "8px 16px 32px" },
  group: { display: "flex", alignItems: "center", gap: "6px", marginTop: "14px", marginBottom: "2px", border: "none", background: "transparent", padding: "2px 0", cursor: "pointer", color: tokens.colorNeutralForeground2, fontFamily: "inherit" },
  groupDanger: { color: tokens.colorPaletteRedForeground1 },
  empty: { display: "flex", flexDirection: "column", alignItems: "center", gap: "8px", padding: "64px 16px", color: tokens.colorNeutralForeground3, textAlign: "center" },
  emptyIcon: { fontSize: "56px", color: tokens.colorBrandForeground2 },
  toolbar: { display: "flex", gap: "8px", marginTop: "8px", flexWrap: "wrap" },
});

interface ViewInfo {
  title: string;
  icon: ReactNode;
  subtitle?: string;
  defaults: Partial<CreateTask>;
  empty: string;
}

export function TaskView({ route, compact }: { route: Extract<Route, { page: "tasks" }>; compact: boolean }) {
  const styles = useStyles();
  const { today, lists, projects, me } = useApp();
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({ erledigt: true });
  const [groupByPerson, setGroupByPerson] = useState(false);
  const [selectedId] = useState<number | null>(null);

  const query = useQuery({
    queryKey: ["tasks", route.view, route.id ?? null, route.q ?? null],
    queryFn: () => api.tasks(route.view, route.id, route.q),
  });

  const info: ViewInfo = useMemo(() => {
    switch (route.view) {
      case "mein-tag":
        return { title: "Mein Tag", icon: <WeatherSunnyRegular />, subtitle: longDate(today), defaults: { myDay: true }, empty: "Nichts für heute geplant. Genieß den Moment – oder plane etwas ein." };
      case "wichtig":
        return { title: "Wichtig", icon: <StarRegular />, defaults: { priority: "hoch" }, empty: "Keine wichtigen Aufgaben offen." };
      case "geplant":
        return { title: "Geplant", icon: <CalendarLtrRegular />, defaults: { dueDate: today }, empty: "Keine Aufgaben mit Termin." };
      case "zugewiesen":
        return { title: "Mir zugewiesen", icon: <PersonArrowLeftRegular />, subtitle: "Aufgaben, die dir andere gegeben haben", defaults: {}, empty: "Niemand hat dir gerade etwas zugewiesen." };
      case "delegiert":
        return { title: "Wartet auf andere", icon: <SendRegular />, subtitle: "Was du delegiert hast – live mit Status", defaults: {}, empty: "Du hast nichts delegiert. Tipp: @Name in der Eingabe." };
      case "wiedervorlagen":
        return { title: "Wiedervorlagen", icon: <ClockAlarmRegular />, subtitle: "Fristen und Dinge, die wieder auf den Tisch müssen", defaults: { kind: "wiedervorlage" }, empty: "Keine Wiedervorlagen." };
      case "erledigt":
        return { title: "Erledigt", icon: <CheckmarkCircleRegular />, subtitle: "Die letzten 30 Tage", defaults: {}, empty: "Noch nichts erledigt." };
      case "liste": {
        const list = lists.find((l) => l.id === route.id);
        return { title: list?.name ?? "Liste", icon: <ListRegular />, defaults: { listId: route.id }, empty: "Diese Liste ist leer." };
      }
      case "projekt": {
        const project = projects.find((p) => p.id === route.id);
        return {
          title: project?.label ?? "Projekt",
          icon: <NumberSymbolRegular />,
          subtitle: project?.teamName ? `${project.teamName} · für alle sichtbar` : "Für alle sichtbar",
          defaults: { projectId: route.id },
          empty: "Noch keine Aufgaben in diesem Projekt.",
        };
      }
      case "suche":
        return { title: `Suche: ${route.q ?? ""}`, icon: <SearchRegular />, defaults: {}, empty: "Keine Treffer." };
    }
  }, [route, today, lists, projects]);

  const groups = useMemo(() => {
    const tasks = query.data ?? [];
    const map = new Map<string, { label: string; danger?: boolean; tasks: Task[] }>();
    const order: string[] = [];
    const push = (key: string, label: string, task: Task, danger = false) => {
      if (!map.has(key)) {
        map.set(key, { label, danger, tasks: [] });
        order.push(key);
      }
      map.get(key)!.tasks.push(task);
    };
    if (groupByPerson) {
      for (const t of tasks) {
        if (t.status === "erledigt") push("erledigt", "Erledigt", t);
        else push(`p${t.assignee.id}`, t.assignee.name, t);
      }
      order.sort((a, b) => (a === "erledigt" ? 1 : b === "erledigt" ? -1 : map.get(a)!.label.localeCompare(map.get(b)!.label)));
    } else {
      const sequence: DueGroup[] = ["ueberfaellig", "heute", "morgen", "woche", "spaeter", "ohne", "erledigt"];
      for (const g of sequence) {
        for (const t of tasks) {
          if (dueGroup(t.dueDate, t.status === "erledigt", t.isOverdue, today) === g) push(g, GROUP_LABEL[g], t, g === "ueberfaellig");
        }
      }
    }
    return order.map((key) => ({ key, ...map.get(key)! }));
  }, [query.data, groupByPerson, today]);

  const showAssignee = route.view === "projekt" || route.view === "delegiert" || route.view === "suche";
  const total = query.data?.filter((t) => t.status !== "erledigt").length ?? 0;

  return (
    <div className={styles.view}>
      <header className={`${styles.header} ${compact ? styles.compactHeader : ""}`}>
        <div className={styles.titleRow} style={{ paddingLeft: compact ? 0 : undefined }}>
          <span className={styles.titleIcon}>{info.icon}</span>
          <Title2 as="h1">{info.title}</Title2>
        </div>
        <Text className={styles.sub}>
          {info.subtitle ? `${info.subtitle} · ` : ""}
          {total} offen
        </Text>
        {route.view === "projekt" && (
          <div className={styles.toolbar}>
            <ToggleButton size="small" icon={<PeopleTeamRegular />} checked={groupByPerson} onClick={() => setGroupByPerson((v) => !v)}>
              Nach Person
            </ToggleButton>
          </div>
        )}
      </header>

      {route.view !== "erledigt" && route.view !== "suche" && (
        <div className={`${styles.add} ${compact ? styles.compactAdd : ""}`}>
          <QuickAdd defaults={info.defaults} />
        </div>
      )}

      <div className={`${styles.list} ${compact ? styles.compactList : ""}`}>
        {query.isLoading && <Spinner style={{ marginTop: 32 }} />}
        {query.error && (
          <MessageBar intent="error">
            <MessageBarBody>{query.error instanceof Error ? query.error.message : "Fehler beim Laden"}</MessageBarBody>
          </MessageBar>
        )}
        {query.data && query.data.length === 0 && (
          <div className={styles.empty}>
            <span className={styles.emptyIcon}>{info.icon}</span>
            <Text size={400}>{info.empty}</Text>
          </div>
        )}
        {groups.map((g) => {
          const isCollapsed = collapsed[g.key] ?? false;
          return (
            <div key={g.key} style={{ display: "contents" }}>
              <button
                className={`${styles.group} ${g.danger ? styles.groupDanger : ""}`}
                onClick={() => setCollapsed((c) => ({ ...c, [g.key]: !isCollapsed }))}
                aria-expanded={!isCollapsed}
              >
                {isCollapsed ? <ChevronRightRegular /> : <ChevronDownRegular />}
                <Text weight="semibold">{g.label}</Text>
                <Text size={200}>{g.tasks.length}</Text>
              </button>
              {!isCollapsed && g.tasks.map((t) => <TaskRow key={t.id} task={t} selected={t.id === selectedId} showAssignee={showAssignee && t.assignee.id !== me.id} />)}
            </div>
          );
        })}
        {route.view === "delegiert" && (query.data?.length ?? 0) > 0 && (
          <Button appearance="transparent" size="small" style={{ alignSelf: "flex-start", marginTop: 16 }} disabled>
            Erledigte Delegationen bleiben 7 Tage sichtbar
          </Button>
        )}
      </div>
    </div>
  );
}
