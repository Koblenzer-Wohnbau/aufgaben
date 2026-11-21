import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  AvatarGroup,
  AvatarGroupItem,
  Button,
  Checkbox,
  makeStyles,
  Menu,
  MenuItemRadio,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  Title2,
  ToggleButton,
  tokens,
} from "@fluentui/react-components";
import {
  ArrowSortRegular,
  CalendarLtrRegular,
  CheckmarkCircleRegular,
  CheckmarkRegular,
  ChevronDownRegular,
  ChevronRightRegular,
  ClockAlarmRegular,
  DeleteRegular,
  EyeRegular,
  HomeRegular,
  HourglassHalfRegular,
  ListRegular,
  MultiselectLtrRegular,
  NumberSymbolRegular,
  PeopleTeamRegular,
  PersonAddRegular,
  PersonArrowLeftRegular,
  SearchRegular,
  SendRegular,
  SnoozeRegular,
  StarRegular,
  WeatherSunnyRegular,
} from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { initials, useApp } from "../context";
import { dueGroup, GROUP_LABEL, longDate, type DueGroup } from "../dates";
import { followUpDue, laterAction, weekdayDate } from "../later";
import type { Route } from "../route";
import { allState, bulkMessage, tasksLabel, toggleSelection, TRASH_DAYS } from "../selection";
import type { BulkAction, CreateTask, Task } from "../types";
import { Confetti } from "./Confetti";
import { EmptyState } from "./EmptyState";
import { LaterMenu } from "./LaterMenu";
import { MyDayHero } from "./MyDayHero";
import { QuickAdd } from "./QuickAdd";
import { ShareListDialog } from "./ShareListDialog";
import { ListMenu } from "./ListMenu";
import { SuggestionsPanel } from "./SuggestionsPanel";
import { TaskRow } from "./TaskRow";

const useStyles = makeStyles({
  view: { flex: 1, display: "flex", flexDirection: "column", overflow: "hidden" },
  header: {
    padding: "20px 32px 8px",
    display: "flex",
    alignItems: "flex-end",
    gap: "12px",
    flexWrap: "wrap",
    // Schmal: links Platz für den Menüknopf lassen.
    "@media (max-width: 760px)": { padding: "14px 16px 8px 56px" },
  },
  compactHeader: { padding: "12px 16px 4px" },
  headText: { flex: 1, minWidth: "200px", display: "flex", flexDirection: "column", gap: "4px" },
  titleRow: { display: "flex", alignItems: "center", gap: "12px" },
  titleIcon: { fontSize: "28px", color: tokens.colorBrandForeground1, display: "flex" },
  sub: { color: tokens.colorNeutralForeground3 },
  tools: { display: "flex", gap: "8px", alignItems: "center" },
  add: { padding: "12px 32px 4px", "@media (max-width: 760px)": { padding: "8px 16px 4px" } },
  compactAdd: { padding: "8px 16px 4px" },
  list: {
    flex: 1,
    overflowY: "auto",
    padding: "8px 32px 48px",
    display: "flex",
    flexDirection: "column",
    gap: "6px",
    "@media (max-width: 760px)": { padding: "8px 16px 32px" },
  },
  compactList: { padding: "8px 16px 32px" },
  group: {
    display: "flex",
    alignItems: "center",
    gap: "6px",
    marginTop: "14px",
    marginBottom: "2px",
    border: "none",
    background: "transparent",
    padding: "2px 0",
    cursor: "pointer",
    color: tokens.colorNeutralForeground2,
    fontFamily: "inherit",
  },
  groupDanger: { color: tokens.colorPaletteRedForeground1 },
  selectBar: {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    gap: "8px 12px",
    margin: "12px 32px 4px",
    padding: "6px 12px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow4,
    "@media (max-width: 760px)": { margin: "8px 16px 4px" },
  },
  compactSelectBar: { margin: "8px 16px 4px" },
  myDayTools: { display: "flex", justifyContent: "flex-end", padding: "4px 32px 0", "@media (max-width: 760px)": { padding: "4px 16px 0" } },
  selectCount: { flex: 1, minWidth: "90px", color: tokens.colorNeutralForeground3 },
});

type SortKey = "faelligkeit" | "wichtigkeit" | "alphabet" | "erstellt";
const SORT_LABEL: Record<SortKey, string> = {
  faelligkeit: "Nach Fälligkeit (gruppiert)",
  wichtigkeit: "Nach Wichtigkeit",
  alphabet: "Alphabetisch",
  erstellt: "Neueste zuerst",
};
const PRIORITY_RANK = { hoch: 0, normal: 1, niedrig: 2 } as const;

function loadSort(view: string): SortKey {
  try {
    const value = localStorage.getItem(`aufgaben.sortierung.${view}`);
    return value && value in SORT_LABEL ? (value as SortKey) : "faelligkeit";
  } catch {
    return "faelligkeit";
  }
}

interface ViewInfo {
  title: string;
  icon: ReactNode;
  subtitle?: string;
  defaults: Partial<CreateTask>;
  empty: string;
}

export function TaskView({ route, compact }: { route: Extract<Route, { page: "tasks" }>; compact: boolean }) {
  const styles = useStyles();
  const { today, lists, projects, me, selectedTaskId, notify } = useApp();
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [groupByPerson, setGroupByPerson] = useState(false);
  const [suggestionsOpen, setSuggestionsOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);
  const [sort, setSort] = useState<SortKey>(() => loadSort(route.view));
  const [confetti, setConfetti] = useState(0);

  // Auswahlmodus: mehrere Aufgaben erledigen, zurückstellen oder in den Papierkorb legen
  const queryClient = useQueryClient();
  const [selecting, setSelecting] = useState(false);
  const [picked, setPicked] = useState<Set<number>>(() => new Set());
  const [anchor, setAnchor] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const exitSelect = () => {
    setSelecting(false);
    setPicked(new Set());
    setAnchor(null);
  };
  useEffect(exitSelect, [route.view, route.id, route.q]);

  useEffect(() => setSort(loadSort(route.view)), [route.view]);

  const query = useQuery({
    queryKey: ["tasks", route.view, route.id ?? null, route.q ?? null],
    queryFn: () => api.tasks(route.view, route.id, route.q),
  });

  const list = route.view === "liste" ? lists.find((l) => l.id === route.id) : undefined;
  const access = useQuery({ queryKey: ["access-mine"], queryFn: api.accessMine, enabled: route.view === "person", staleTime: 60_000 });
  const grant = route.view === "person" ? access.data?.find((a) => a.owner.id === route.id) : undefined;

  const info: ViewInfo = useMemo(() => {
    switch (route.view) {
      case "mein-tag":
        return { title: "Mein Tag", icon: <WeatherSunnyRegular />, subtitle: longDate(today), defaults: { myDay: true }, empty: "Nichts für heute geplant. Hol dir oben Vorschläge oder plane etwas ein." };
      case "aufgaben":
        return { title: "Aufgaben", icon: <HomeRegular />, subtitle: "Alles, was dir gehört und in keiner Liste steckt", defaults: {}, empty: "Keine losen Aufgaben – alles ist einsortiert." };
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
      case "warteschleife":
        return {
          title: "Warteschleife",
          icon: <HourglassHalfRegular />,
          subtitle: "Warte auf Antwort und Zurückgestelltes – kommt am Tag von selbst in „Mein Tag“ zurück",
          defaults: {},
          empty: "Nichts in der Warteschleife. In einer Aufgabe auf „Warte auf …“ oder „Später“ klicken.",
        };
      case "erledigt":
        return { title: "Erledigt", icon: <CheckmarkCircleRegular />, subtitle: "Die letzten 30 Tage", defaults: {}, empty: "Noch nichts erledigt." };
      case "liste":
        return {
          title: list?.name ?? "Liste",
          icon: <ListRegular />,
          subtitle: list && !list.isOwner ? `Geteilt von ${list.owner.name}` : list && list.members.length > 0 ? "Geteilte Liste" : undefined,
          defaults: { listId: route.id },
          empty: "Diese Liste ist leer.",
        };
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
      case "papierkorb":
        // Eigene Ansicht (TrashView); hier nur, damit jede Ansicht einen Titel hat.
        return { title: "Papierkorb", icon: <DeleteRegular />, defaults: {}, empty: "Der Papierkorb ist leer." };
      case "person":
        return {
          title: grant ? `Aufgaben von ${grant.owner.name}` : "Freigegebene Aufgaben",
          icon: <EyeRegular />,
          subtitle: `Einsicht ${grant?.permission === "bearbeiten" ? "zum Lesen und Bearbeiten" : "nur zum Lesen"}${grant?.validUntil ? ` bis ${grant.validUntil.split("-").reverse().join(".")}` : ""} · Zugriffe werden protokolliert`,
          defaults: {},
          empty: "Keine offenen Aufgaben.",
        };
    }
  }, [route, today, list, projects, grant]);

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
    const openTasks = tasks.filter((t) => t.status !== "erledigt");
    const doneTasks = tasks.filter((t) => t.status === "erledigt");

    if (route.view === "warteschleife") {
      // Reihenfolge vom Server: was zuerst wieder dran ist, steht oben.
      for (const t of openTasks) if (followUpDue(t, today)) push("nachhaken", "Jetzt nachhaken", t, true);
      for (const t of openTasks) if (t.waitingFor && !followUpDue(t, today)) push("warten", "Warte auf Antwort", t);
      for (const t of openTasks) if (!t.waitingFor) push("zurueckgestellt", "Zurückgestellt", t);
    } else if (groupByPerson) {
      for (const t of openTasks) push(`p${t.assignee.id}`, t.assignee.name, t);
      order.sort((a, b) => map.get(a)!.label.localeCompare(map.get(b)!.label));
    } else if (sort === "faelligkeit") {
      const sequence: DueGroup[] = ["ueberfaellig", "heute", "morgen", "woche", "spaeter", "ohne"];
      for (const g of sequence) {
        for (const t of openTasks) {
          if (dueGroup(t.dueDate, false, t.isOverdue, today) === g) push(g, GROUP_LABEL[g], t, g === "ueberfaellig");
        }
      }
    } else {
      const sorted = [...openTasks].sort((a, b) => {
        if (sort === "wichtigkeit") return PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority] || (a.dueDate ?? "9999").localeCompare(b.dueDate ?? "9999");
        if (sort === "alphabet") return a.title.localeCompare(b.title, "de");
        return b.createdAt.localeCompare(a.createdAt);
      });
      for (const t of sorted) push("offen", "Offen", t);
    }
    for (const t of doneTasks) push("erledigt", "Erledigt", t);
    return order.map((key) => ({ key, ...map.get(key)! }));
  }, [query.data, groupByPerson, sort, today, route.view]);

  const isCollapsedGroup = (key: string) => collapsed[key] ?? (key === "erledigt" && route.view !== "erledigt");
  const allIds = useMemo(() => (query.data ?? []).map((t) => t.id), [query.data]);
  // Nur, was noch in der Ansicht steht – Gelöschtes oder Weggefallenes zählt nicht mit
  const chosen = useMemo(() => (query.data ?? []).filter((t) => picked.has(t.id)), [query.data, picked]);
  const visibleOrder = groups.filter((g) => !isCollapsedGroup(g.key)).flatMap((g) => g.tasks.map((t) => t.id));

  const refreshAll = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["tasks"] }),
      queryClient.invalidateQueries({ queryKey: ["counts"] }),
      queryClient.invalidateQueries({ queryKey: ["task"] }),
    ]);

  const select = (id: number, range: boolean) => {
    setPicked((p) => toggleSelection(p, visibleOrder, id, anchor, range));
    setAnchor(id);
  };

  const runBulk = async (action: BulkAction, ids: number[]) => {
    if (ids.length === 0) return;
    setBusy(true);
    try {
      const result = await api.bulkTasks(action, ids);
      notify(bulkMessage(action, result), result.done === 0 ? "error" : "success");
      setPicked(new Set());
      await refreshAll();
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    } finally {
      setBusy(false);
    }
  };

  const completePicked = () => runBulk("erledigen", chosen.filter((t) => t.status !== "erledigt").map((t) => t.id));

  const deletePicked = () => {
    if (chosen.length === 0) return;
    if (!window.confirm(`${tasksLabel(chosen.length)} in den Papierkorb legen? Dort lassen sie sich ${TRASH_DAYS} Tage lang wiederherstellen.`)) return;
    void runBulk("loeschen", chosen.map((t) => t.id));
  };

  const laterPicked = async (date: string) => {
    const own = chosen.filter((t) => t.status !== "erledigt" && t.assignee.id === me.id);
    if (own.length === 0) return;
    setBusy(true);
    let ok = 0;
    for (const t of own) {
      try {
        await api.patchTask(t.id, laterAction(t, date, today).patch);
        ok++;
      } catch {
        /* z. B. inzwischen erledigt – wird unten mitgezählt */
      }
    }
    setBusy(false);
    setPicked(new Set());
    notify(`${tasksLabel(ok)} auf ${weekdayDate(date)} gelegt${ok < own.length ? ` – ${own.length - ok} ging nicht` : ""}`, ok ? "success" : "error");
    await refreshAll();
  };

  const deleteCompleted = async () => {
    if (!window.confirm(`Alle erledigten Aufgaben, die du angelegt hast, in den Papierkorb legen? Dort lassen sie sich ${TRASH_DAYS} Tage lang wiederherstellen.`)) return;
    try {
      const result = await api.deleteCompleted();
      notify(
        result.done === 0
          ? "Keine erledigten Aufgaben, die du angelegt hast"
          : `${tasksLabel(result.done)} in den Papierkorb gelegt${result.skipped ? ` – ${result.skipped} von anderen angelegte bleiben` : ""}`,
        result.done === 0 ? "info" : "success",
      );
      await refreshAll();
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  // Tastatur im Auswahlmodus: Esc beendet, Entf legt in den Papierkorb, Strg+A wählt alles aus.
  const keys = useRef({ deletePicked, allIds });
  keys.current = { deletePicked, allIds };
  useEffect(() => {
    if (!selecting) return;
    const onKey = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement | null)?.closest("input, textarea, [contenteditable='true']")) return;
      if (e.key === "Escape") {
        exitSelect();
      } else if (e.key === "Delete") {
        e.preventDefault();
        keys.current.deletePicked();
      } else if (e.key.toLowerCase() === "a" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        setPicked(new Set(keys.current.allIds));
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [selecting]);

  const selectToggle = (query.data?.length ?? 0) > 0 && (
    <ToggleButton
      icon={<MultiselectLtrRegular />}
      checked={selecting}
      appearance={selecting ? "primary" : "subtle"}
      onClick={() => (selecting ? exitSelect() : setSelecting(true))}
      title="Mehrere Aufgaben auswählen – Umschalt+Klick wählt einen Bereich"
    >
      Auswählen
    </ToggleButton>
  );

  // Konfetti, sobald der letzte Punkt von "Mein Tag" erledigt ist.
  const openCount = query.data?.filter((t) => t.status !== "erledigt").length ?? 0;
  const doneCount = query.data?.filter((t) => t.status === "erledigt").length ?? 0;
  const previousOpen = useRef<number | null>(null);
  useEffect(() => {
    if (route.view !== "mein-tag" || !query.data) return;
    if (previousOpen.current !== null && previousOpen.current > 0 && openCount === 0 && doneCount > 0) {
      setConfetti((c) => c + 1);
      notify("Mein Tag ist erledigt – stark!");
    }
    previousOpen.current = openCount;
  }, [openCount, doneCount, route.view, query.data, notify]);
  useEffect(() => {
    previousOpen.current = null;
  }, [route.view]);

  const showAssignee = route.view === "projekt" || route.view === "delegiert" || route.view === "suche" || (route.view === "liste" && !!list && (list.members.length > 0 || !list.isOwner));
  const myDay = route.view === "mein-tag";

  const changeSort = (value: SortKey) => {
    setSort(value);
    try {
      localStorage.setItem(`aufgaben.sortierung.${route.view}`, value);
    } catch {
      /* privater Modus – Sortierung gilt nur bis zum Neuladen */
    }
  };

  return (
    <div className={styles.view}>
      {myDay ? (
        <MyDayHero
          name={me.name}
          today={today}
          done={doneCount}
          total={openCount + doneCount}
          compact={compact}
          suggestionsOpen={suggestionsOpen}
          onToggleSuggestions={() => setSuggestionsOpen((v) => !v)}
        />
      ) : (
        <header className={`${styles.header} ${compact ? styles.compactHeader : ""}`}>
          <div className={styles.headText}>
            <div className={styles.titleRow}>
              <span className={styles.titleIcon}>{info.icon}</span>
              <Title2 as="h1">{info.title}</Title2>
            </div>
            <Text className={styles.sub}>
              {info.subtitle ? `${info.subtitle} · ` : ""}
              {route.view === "erledigt" ? `${doneCount} erledigt` : `${openCount} offen`}
            </Text>
          </div>
          <div className={styles.tools}>
            {list && (list.members.length > 0 || !list.isOwner) && (
              <AvatarGroup size={24} layout="stack">
                {[list.owner, ...list.members].slice(0, 5).map((p) => (
                  <AvatarGroupItem key={p.id} name={p.name} initials={initials(p.name)} />
                ))}
              </AvatarGroup>
            )}
            {list && (
              <Button icon={<PersonAddRegular />} onClick={() => setShareOpen(true)}>
                {list.isOwner ? "Teilen" : "Mitglieder"}
              </Button>
            )}
            {list?.isOwner && <ListMenu list={list} />}
            {route.view === "projekt" && (
              <ToggleButton icon={<PeopleTeamRegular />} checked={groupByPerson} onClick={() => setGroupByPerson((v) => !v)}>
                Nach Person
              </ToggleButton>
            )}
            {route.view === "erledigt" && doneCount > 0 && !selecting && (
              <Button icon={<DeleteRegular />} appearance="subtle" onClick={() => void deleteCompleted()}>
                Erledigte löschen
              </Button>
            )}
            {selectToggle}
            {route.view !== "erledigt" && route.view !== "warteschleife" && (
              <Menu>
                <MenuTrigger disableButtonEnhancement>
                  <Button icon={<ArrowSortRegular />} appearance="subtle" aria-label="Sortierung">
                    {sort === "faelligkeit" ? "" : "Sortiert"}
                  </Button>
                </MenuTrigger>
                <MenuPopover>
                  <MenuList checkedValues={{ sort: [sort] }} onCheckedValueChange={(_, d) => changeSort(d.checkedItems[0] as SortKey)}>
                    {(Object.keys(SORT_LABEL) as SortKey[]).map((k) => (
                      <MenuItemRadio key={k} name="sort" value={k}>
                        {SORT_LABEL[k]}
                      </MenuItemRadio>
                    ))}
                  </MenuList>
                </MenuPopover>
              </Menu>
            )}
          </div>
        </header>
      )}

      {myDay && suggestionsOpen && <SuggestionsPanel compact={compact} onClose={() => setSuggestionsOpen(false)} />}

      {myDay && !selecting && selectToggle && <div className={`${styles.myDayTools} ${compact ? styles.compactAdd : ""}`}>{selectToggle}</div>}

      {selecting ? (
        <div className={`${styles.selectBar} ${compact ? styles.compactSelectBar : ""}`} role="toolbar" aria-label="Ausgewählte Aufgaben">
          <Checkbox
            shape="circular"
            checked={allState(picked, allIds)}
            onChange={(_, d) => setPicked(d.checked === true ? new Set(allIds) : new Set())}
            label={`Alle (${allIds.length})`}
          />
          <Text className={styles.selectCount}>{chosen.length === 0 ? "Nichts ausgewählt" : `${chosen.length} ausgewählt`}</Text>
          <Button icon={<CheckmarkRegular />} disabled={busy || !chosen.some((t) => t.status !== "erledigt")} onClick={() => void completePicked()}>
            Erledigen
          </Button>
          <LaterMenu
            trigger={
              <Button icon={<SnoozeRegular />} disabled={busy || !chosen.some((t) => t.status !== "erledigt" && t.assignee.id === me.id)}>
                Später
              </Button>
            }
            onPick={(date) => void laterPicked(date)}
          />
          <Button icon={<DeleteRegular />} disabled={busy || chosen.length === 0} onClick={deletePicked}>
            Löschen
          </Button>
          <Button appearance="subtle" onClick={exitSelect}>
            Fertig
          </Button>
        </div>
      ) : (
        route.view !== "erledigt" &&
        route.view !== "suche" &&
        route.view !== "person" &&
        route.view !== "warteschleife" && (
          <div className={`${styles.add} ${compact ? styles.compactAdd : ""}`}>
            <QuickAdd defaults={info.defaults} />
          </div>
        )
      )}

      <div className={`${styles.list} ${compact ? styles.compactList : ""}`}>
        {query.isLoading && <Spinner style={{ marginTop: 32 }} />}
        {query.error && (
          <MessageBar intent="error">
            <MessageBarBody>{query.error instanceof Error ? query.error.message : "Fehler beim Laden"}</MessageBarBody>
          </MessageBar>
        )}
        {query.data && query.data.length === 0 && <EmptyState text={info.empty} sunny={myDay} />}
        {groups.map((g) => {
          // Erledigte sind sonst eingeklappt – in der Ansicht „Erledigt“ sind sie der Inhalt.
          const isCollapsed = isCollapsedGroup(g.key);
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
              {!isCollapsed &&
                g.tasks.map((t) => (
                  <TaskRow
                    key={t.id}
                    task={t}
                    selected={t.id === selectedTaskId}
                    showAssignee={showAssignee && t.assignee.id !== me.id}
                    selecting={selecting}
                    checked={picked.has(t.id)}
                    onSelect={select}
                  />
                ))}
            </div>
          );
        })}
      </div>
      {shareOpen && list && <ShareListDialog list={list} onClose={() => setShareOpen(false)} />}
      <Confetti fire={confetti} />
    </div>
  );
}
