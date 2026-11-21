import { useState, type DragEvent, type ReactNode } from "react";
import {
  Avatar,
  Button,
  CounterBadge,
  Input,
  makeStyles,
  mergeClasses,
  Text,
  Tooltip,
  tokens,
} from "@fluentui/react-components";
import {
  AddRegular,
  BeachRegular,
  CalendarLtrRegular,
  CheckmarkCircleRegular,
  ClockAlarmRegular,
  DeleteRegular,
  DismissRegular,
  DocumentBulletListRegular,
  EyeRegular,
  HomeRegular,
  HourglassHalfRegular,
  ListRegular,
  NumberSymbolRegular,
  PeopleRegular,
  PeopleSwapRegular,
  PersonArrowLeftRegular,
  SearchRegular,
  SendRegular,
  SettingsRegular,
  ShieldPersonRegular,
  StarRegular,
  WeatherSunnyRegular,
} from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { initials, useApp } from "../context";
import { draggedTaskId, dropPatch, isTaskDrag } from "../dnd";
import { weekdayDate } from "../later";
import { hrefFor, navigate, type Route } from "../route";
import type { Project } from "../types";

const useStyles = makeStyles({
  nav: {
    width: "264px",
    flexShrink: 0,
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground2,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: "hidden",
  },
  overlay: { position: "fixed", inset: "0 auto 0 0", zIndex: 20, boxShadow: tokens.shadow28 },
  backdrop: { position: "fixed", inset: 0, zIndex: 19, backgroundColor: "rgba(0,0,0,0.3)" },
  header: { display: "flex", alignItems: "center", gap: "10px", padding: "16px 16px 8px" },
  appIcon: { fontSize: "24px", color: tokens.colorBrandForeground1 },
  search: { margin: "4px 12px 8px" },
  scroll: { flex: 1, overflowY: "auto", padding: "4px 8px 12px" },
  section: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: "16px 8px 4px",
    color: tokens.colorNeutralForeground3,
  },
  item: {
    display: "flex",
    alignItems: "center",
    gap: "10px",
    padding: "7px 10px",
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground1,
    textDecorationLine: "none",
    fontSize: tokens.fontSizeBase300,
    cursor: "pointer",
    transitionProperty: "background-color, box-shadow, transform",
    transitionDuration: tokens.durationFaster,
    ":hover": { backgroundColor: tokens.colorSubtleBackgroundHover },
  },
  active: {
    backgroundColor: tokens.colorSubtleBackgroundSelected,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1,
  },
  dropTarget: {
    backgroundColor: tokens.colorBrandBackground2,
    boxShadow: `inset 0 0 0 2px ${tokens.colorBrandStroke1}`,
    transform: "scale(1.02)",
  },
  label: { flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
  icon: { fontSize: "20px", flexShrink: 0, display: "flex" },
  shared: { fontSize: "14px", color: tokens.colorNeutralForeground3 },
  team: { padding: "8px 10px 2px", color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  footer: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    padding: "10px 12px",
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  dot: { width: "8px", height: "8px", borderRadius: "50%", flexShrink: 0 },
  action: {
    width: "100%",
    border: "none",
    background: "transparent",
    fontFamily: "inherit",
    textAlign: "left",
  },
  newList: { margin: "4px 2px" },
});

const COLOR_DOT: Record<string, string> = {
  blau: "#4f6bed",
  gruen: "#13a10e",
  rot: "#d13438",
  orange: "#ca5010",
  lila: "#8764b8",
  grau: "#7a7574",
};

interface NavItemProps {
  to: Route;
  active: boolean;
  icon: ReactNode;
  label: string;
  count?: number;
  accent?: boolean;
  extra?: ReactNode;
  onNavigate: () => void;
}

/** Eintrag der Seitenleiste – zugleich Ablageziel für gezogene Aufgaben. */
function NavItem({ to, active, icon, label, count, accent, extra, onNavigate }: NavItemProps) {
  const styles = useStyles();
  const { today, notify } = useApp();
  const queryClient = useQueryClient();
  const [over, setOver] = useState(false);
  const drop = dropPatch(to, today);

  const onDragOver = (e: DragEvent) => {
    if (!drop || !isTaskDrag(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
    setOver(true);
  };
  const onDrop = async (e: DragEvent) => {
    setOver(false);
    const id = draggedTaskId(e);
    if (!drop || id === null) return;
    e.preventDefault();
    try {
      await (drop.remove ? api.deleteTask(id) : api.patchTask(id, drop.patch ?? {}));
      notify(drop.label);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["tasks"] }),
        queryClient.invalidateQueries({ queryKey: ["counts"] }),
        queryClient.invalidateQueries({ queryKey: ["task", id] }),
      ]);
    } catch (err) {
      notify(err instanceof Error ? err.message : "Verschieben fehlgeschlagen", "error");
    }
  };

  return (
    <a
      href={hrefFor(to)}
      className={mergeClasses(styles.item, active && styles.active, over && styles.dropTarget)}
      onClick={onNavigate}
      onDragOver={onDragOver}
      onDragLeave={() => setOver(false)}
      onDrop={onDrop}
    >
      <span className={styles.icon}>{icon}</span>
      <span className={styles.label}>{label}</span>
      {extra}
      {!!count && <CounterBadge count={count} size="small" appearance={accent ? "filled" : "ghost"} color={accent ? "danger" : "informative"} />}
    </a>
  );
}

interface Props {
  route: Route;
  open: boolean;
  overlay: boolean;
  online: boolean;
  onClose: () => void;
  onSettings: () => void;
  onAbsence: () => void;
}

export function Sidebar({ route, open, overlay, online, onClose, onSettings, onAbsence }: Props) {
  const styles = useStyles();
  const { me, projects, lists, notify } = useApp();
  const counts = useQuery({ queryKey: ["counts"], queryFn: api.counts, refetchInterval: 60_000 });
  const access = useQuery({ queryKey: ["access-mine"], queryFn: api.accessMine, staleTime: 60_000 });
  const absences = useQuery({ queryKey: ["absences"], queryFn: api.absenceStatus, staleTime: 60_000 });
  const queryClient = useQueryClient();
  const [search, setSearch] = useState(route.page === "tasks" && route.view === "suche" ? route.q ?? "" : "");
  const [newList, setNewList] = useState<string | null>(null);

  if (!open) return null;

  const c = counts.data;
  const current = hrefFor(route);
  const item = (to: Route, icon: ReactNode, label: string, count?: number, accent?: boolean, extra?: ReactNode, key?: string | number) => (
    <NavItem
      key={key ?? hrefFor(to)}
      to={to}
      active={hrefFor(to) === current}
      icon={icon}
      label={label}
      count={count}
      accent={accent}
      extra={extra}
      onNavigate={() => overlay && onClose()}
    />
  );

  const byTeam = new Map<string, Project[]>();
  for (const p of projects) {
    const key = p.teamName ?? "Weitere Projekte";
    byTeam.set(key, [...(byTeam.get(key) ?? []), p]);
  }

  const createList = async () => {
    const name = newList?.trim();
    setNewList(null);
    if (!name) return;
    try {
      const list = await api.createList(name);
      await queryClient.invalidateQueries({ queryKey: ["lists"] });
      navigate({ page: "tasks", view: "liste", id: list.id });
    } catch (e) {
      notify(e instanceof Error ? e.message : "Liste konnte nicht angelegt werden", "error");
    }
  };

  const nav = (
    <nav className={mergeClasses(styles.nav, overlay && styles.overlay)} aria-label="Ansichten">
      <div className={styles.header}>
        <CheckmarkCircleRegular className={styles.appIcon} />
        <Text weight="semibold" size={500} style={{ flex: 1 }}>
          Aufgaben
        </Text>
        {overlay && <Button appearance="subtle" icon={<DismissRegular />} aria-label="Schließen" onClick={onClose} />}
      </div>
      <form
        className={styles.search}
        onSubmit={(e) => {
          e.preventDefault();
          navigate({ page: "tasks", view: "suche", q: search });
          if (overlay) onClose();
        }}
      >
        <Input
          contentBefore={<SearchRegular />}
          placeholder="Suchen ( / )"
          aria-label="Suchen"
          value={search}
          onChange={(_, d) => setSearch(d.value)}
          style={{ width: "100%" }}
        />
      </form>
      <div className={styles.scroll}>
        {item({ page: "tasks", view: "mein-tag" }, <WeatherSunnyRegular />, "Mein Tag", c?.myDay)}
        {item({ page: "tasks", view: "wichtig" }, <StarRegular />, "Wichtig", c?.important)}
        {item({ page: "tasks", view: "geplant" }, <CalendarLtrRegular />, "Geplant", c?.planned, !!c?.overdue)}
        {item({ page: "tasks", view: "aufgaben" }, <HomeRegular />, "Aufgaben", c?.inbox)}
        {item({ page: "tasks", view: "zugewiesen" }, <PersonArrowLeftRegular />, "Mir zugewiesen", c?.assigned)}
        {item({ page: "tasks", view: "delegiert" }, <SendRegular />, "Wartet auf andere", c?.delegated)}
        {item({ page: "tasks", view: "warteschleife" }, <HourglassHalfRegular />, "Warteschleife", c?.waiting, !!c?.waitingDue)}
        {item({ page: "tasks", view: "wiedervorlagen" }, <ClockAlarmRegular />, "Wiedervorlagen", c?.followUps)}
        {item({ page: "tasks", view: "erledigt" }, <CheckmarkCircleRegular />, "Erledigt")}
        {item({ page: "tasks", view: "papierkorb" }, <DeleteRegular />, "Papierkorb", c?.trash)}
        <button
          type="button"
          className={mergeClasses(styles.item, styles.action)}
          onClick={() => {
            onAbsence();
            if (overlay) onClose();
          }}
        >
          <span className={styles.icon}>
            <BeachRegular />
          </span>
          <span className={styles.label}>
            {absences.data?.current
              ? absences.data.current.started
                ? `Im Urlaub bis ${weekdayDate(absences.data.current.until)}`
                : `Urlaub ab ${weekdayDate(absences.data.current.from)}`
              : "Urlaub und Vertretung"}
          </span>
        </button>
        {item({ page: "vorlagen" }, <DocumentBulletListRegular />, "Vorlagen")}
        {me.isAdmin && item({ page: "verwaltung" }, <ShieldPersonRegular />, "Verwaltung")}

        <div className={styles.section}>
          <Text size={200} weight="semibold">
            Listen
          </Text>
          <Tooltip content="Neue Liste" relationship="label">
            <Button size="small" appearance="subtle" icon={<AddRegular />} onClick={() => setNewList("")} />
          </Tooltip>
        </div>
        {newList !== null && (
          <Input
            className={styles.newList}
            autoFocus
            placeholder="Name der Liste"
            value={newList}
            onChange={(_, d) => setNewList(d.value)}
            onBlur={createList}
            onKeyDown={(e) => {
              if (e.key === "Enter") void createList();
              if (e.key === "Escape") setNewList(null);
            }}
          />
        )}
        {lists.map((l) =>
          item(
            { page: "tasks", view: "liste", id: l.id },
            <ListRegular style={{ color: COLOR_DOT[l.color] }} />,
            l.name,
            c?.lists[l.id],
            false,
            l.members.length > 0 || !l.isOwner ? (
              <Tooltip content={l.isOwner ? `Geteilt mit ${l.members.length} Person(en)` : `Geteilt von ${l.owner.name}`} relationship="label">
                <PeopleRegular className={styles.shared} />
              </Tooltip>
            ) : undefined,
            `l${l.id}`,
          ),
        )}

        {(absences.data?.deputy.length ?? 0) > 0 && (
          <>
            <div className={styles.section}>
              <Text size={200} weight="semibold">
                Vertretung
              </Text>
            </div>
            {absences.data!.deputy.map((d) =>
              item(
                { page: "tasks", view: "vertretung", id: d.absenceId },
                <PeopleSwapRegular />,
                `Für ${d.person.name}`,
                d.open,
                false,
                undefined,
                `v${d.absenceId}`,
              ),
            )}
          </>
        )}

        {(access.data?.length ?? 0) > 0 && (
          <>
            <div className={styles.section}>
              <Text size={200} weight="semibold">
                Freigegeben für mich
              </Text>
            </div>
            {access.data!.map((a) =>
              item(
                { page: "tasks", view: "person", id: a.owner.id },
                <EyeRegular />,
                a.owner.name,
                undefined,
                false,
                undefined,
                `g${a.grantId}`,
              ),
            )}
          </>
        )}

        <div className={styles.section}>
          <Text size={200} weight="semibold">
            Projektkanäle
          </Text>
        </div>
        {[...byTeam.entries()].map(([team, items]) => (
          <div key={team}>
            <div className={styles.team}>{team}</div>
            {items.map((p) => item({ page: "tasks", view: "projekt", id: p.id }, <NumberSymbolRegular />, p.label, c?.projects[p.id], false, undefined, `p${p.id}`))}
          </div>
        ))}
      </div>
      <div className={styles.footer}>
        <Avatar name={me.name} initials={initials(me.name)} size={28} color="colorful" />
        <Text size={200} style={{ flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {me.name}
        </Text>
        <Tooltip content={online ? "Live verbunden" : "Verbindung wird aufgebaut …"} relationship="label">
          <span className={styles.dot} style={{ backgroundColor: online ? "#13a10e" : "#c19c00" }} />
        </Tooltip>
        <Tooltip content="Einstellungen" relationship="label">
          <Button appearance="subtle" icon={<SettingsRegular />} onClick={onSettings} />
        </Tooltip>
      </div>
    </nav>
  );

  return overlay ? (
    <>
      <div className={styles.backdrop} onClick={onClose} />
      {nav}
    </>
  ) : (
    nav
  );
}
