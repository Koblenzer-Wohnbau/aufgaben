import { useState } from "react";
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
  ClockAlarmRegular,
  CalendarLtrRegular,
  CheckmarkCircleRegular,
  DismissRegular,
  DocumentBulletListRegular,
  ListRegular,
  NumberSymbolRegular,
  PersonArrowLeftRegular,
  SearchRegular,
  SendRegular,
  SettingsRegular,
  StarRegular,
  WeatherSunnyRegular,
} from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { api } from "../api";
import { initials, useApp } from "../context";
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
  overlay: {
    position: "fixed",
    inset: "0 auto 0 0",
    zIndex: 20,
    boxShadow: tokens.shadow28,
  },
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
    ":hover": { backgroundColor: tokens.colorSubtleBackgroundHover },
  },
  active: {
    backgroundColor: tokens.colorSubtleBackgroundSelected,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1,
  },
  label: { flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
  icon: { fontSize: "20px", flexShrink: 0 },
  team: { padding: "8px 10px 2px", color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  footer: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    padding: "10px 12px",
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  dot: { width: "8px", height: "8px", borderRadius: "50%", flexShrink: 0 },
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

interface Props {
  route: Route;
  open: boolean;
  overlay: boolean;
  online: boolean;
  onClose: () => void;
  onSettings: () => void;
}

export function Sidebar({ route, open, overlay, online, onClose, onSettings }: Props) {
  const styles = useStyles();
  const { me, projects, lists, notify } = useApp();
  const counts = useQuery({ queryKey: ["counts"], queryFn: api.counts, refetchInterval: 60_000 });
  const queryClient = useQueryClient();
  const [search, setSearch] = useState(route.page === "tasks" && route.view === "suche" ? route.q ?? "" : "");
  const [newList, setNewList] = useState<string | null>(null);

  if (!open) return null;

  const c = counts.data;
  const isActive = (r: Route) => hrefFor(r) === hrefFor(route);

  const Item = ({ to, icon, label, count, accent }: { to: Route; icon: ReactNode; label: string; count?: number; accent?: boolean }) => (
    <a
      href={hrefFor(to)}
      className={mergeClasses(styles.item, isActive(to) && styles.active)}
      onClick={() => overlay && onClose()}
    >
      <span className={styles.icon}>{icon}</span>
      <span className={styles.label}>{label}</span>
      {!!count && <CounterBadge count={count} size="small" appearance={accent ? "filled" : "ghost"} color={accent ? "danger" : "informative"} />}
    </a>
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
          placeholder="Suchen"
          value={search}
          onChange={(_, d) => setSearch(d.value)}
          style={{ width: "100%" }}
        />
      </form>
      <div className={styles.scroll}>
        <Item to={{ page: "tasks", view: "mein-tag" }} icon={<WeatherSunnyRegular />} label="Mein Tag" count={c?.myDay} />
        <Item to={{ page: "tasks", view: "wichtig" }} icon={<StarRegular />} label="Wichtig" count={c?.important} />
        <Item to={{ page: "tasks", view: "geplant" }} icon={<CalendarLtrRegular />} label="Geplant" count={c?.planned} accent={!!c?.overdue} />
        <Item to={{ page: "tasks", view: "zugewiesen" }} icon={<PersonArrowLeftRegular />} label="Mir zugewiesen" count={c?.assigned} />
        <Item to={{ page: "tasks", view: "delegiert" }} icon={<SendRegular />} label="Wartet auf andere" count={c?.delegated} />
        <Item to={{ page: "tasks", view: "wiedervorlagen" }} icon={<ClockAlarmRegular />} label="Wiedervorlagen" count={c?.followUps} />
        <Item to={{ page: "vorlagen" }} icon={<DocumentBulletListRegular />} label="Vorlagen" />

        <div className={styles.section}>
          <Text size={200} weight="semibold">
            Meine Listen
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
        {lists.map((l) => (
          <Item
            key={l.id}
            to={{ page: "tasks", view: "liste", id: l.id }}
            icon={<ListRegular style={{ color: COLOR_DOT[l.color] }} />}
            label={l.name}
            count={c?.lists[l.id]}
          />
        ))}

        <div className={styles.section}>
          <Text size={200} weight="semibold">
            Projektkanäle
          </Text>
        </div>
        {[...byTeam.entries()].map(([team, items]) => (
          <div key={team}>
            <div className={styles.team}>{team}</div>
            {items.map((p) => (
              <Item
                key={p.id}
                to={{ page: "tasks", view: "projekt", id: p.id }}
                icon={<NumberSymbolRegular />}
                label={p.label}
                count={c?.projects[p.id]}
              />
            ))}
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
