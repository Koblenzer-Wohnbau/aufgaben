import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Button,
  makeStyles,
  Spinner,
  Toast,
  Toaster,
  ToastTitle,
  tokens,
  useId,
  useToastController,
} from "@fluentui/react-components";
import { NavigationRegular } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "./api";
import { AppContext, type AppData } from "./context";
import { connectLive } from "./realtime";
import { useRoute } from "./route";
import { inChannelTab, inTeams, type TeamsInfo } from "./teams";
import { LoginScreen } from "./components/LoginScreen";
import { Sidebar } from "./components/Sidebar";
import { TaskView } from "./components/TaskView";
import { TaskDrawer } from "./components/TaskDrawer";
import { TemplatesView } from "./components/TemplatesView";
import { SettingsDialog } from "./components/SettingsDialog";
import { ChannelConfig } from "./components/ChannelConfig";

const useStyles = makeStyles({
  shell: {
    display: "flex",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground3,
  },
  main: {
    flex: 1,
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
  },
  menuButton: {
    position: "absolute",
    top: "12px",
    left: "8px",
    zIndex: 2,
    "@media (min-width: 761px)": { display: "none" },
  },
});

function useMedia(query: string) {
  const [match, setMatch] = useState(() => window.matchMedia(query).matches);
  useEffect(() => {
    const mq = window.matchMedia(query);
    const handler = () => setMatch(mq.matches);
    mq.addEventListener("change", handler);
    return () => mq.removeEventListener("change", handler);
  }, [query]);
  return match;
}

function useIsNarrow() {
  const narrow = useMedia("(max-width: 760px)");
  return narrow;
}

export function App({ teams }: { teams: TeamsInfo | null }) {
  const me = useQuery({ queryKey: ["me"], queryFn: api.me, retry: false });

  if (me.isLoading) {
    return (
      <div className="center-screen">
        <Spinner label="Lade Aufgaben …" />
      </div>
    );
  }
  if (me.error instanceof ApiError && me.error.status === 401) {
    return <LoginScreen />;
  }
  if (me.error || !me.data) {
    return (
      <div className="center-screen">
        {me.error instanceof Error ? me.error.message : "Unbekannter Fehler"}
      </div>
    );
  }
  return <Shell teams={teams} />;
}

function Shell({ teams }: { teams: TeamsInfo | null }) {
  const styles = useStyles();
  const route = useRoute();
  const queryClient = useQueryClient();
  const toasterId = useId("toaster");
  const { dispatchToast } = useToastController(toasterId);
  const narrow = useIsNarrow();
  const drawerOverlay = useMedia("(max-width: 1100px)");
  const [navOpen, setNavOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [online, setOnline] = useState(true);
  const [taskId, setTaskId] = useState<number | null>(() => {
    if (route.page === "tasks" && route.taskId) return route.taskId;
    const sub = teams?.subPageId;
    return sub?.startsWith("task-") ? Number(sub.slice(5)) : null;
  });

  const me = useQuery({ queryKey: ["me"], queryFn: api.me });
  const projects = useQuery({ queryKey: ["projects"], queryFn: api.projects });
  const users = useQuery({ queryKey: ["users"], queryFn: () => api.users() });
  const lists = useQuery({ queryKey: ["lists"], queryFn: api.lists });

  useEffect(() => {
    if (route.page === "tasks" && route.taskId) setTaskId(route.taskId);
  }, [route]);

  useEffect(
    () =>
      connectLive(
        (m) => {
          if (m.type === "task" || m.type === "task-deleted") {
            void queryClient.invalidateQueries({ queryKey: ["tasks"] });
            void queryClient.invalidateQueries({ queryKey: ["counts"] });
            void queryClient.invalidateQueries({ queryKey: ["projects"] });
            void queryClient.invalidateQueries({ queryKey: ["lists"] });
            if (m.id) void queryClient.invalidateQueries({ queryKey: ["task", m.id] });
          }
          if (m.type === "verbunden") {
            void queryClient.invalidateQueries();
          }
        },
        setOnline,
      ),
    [queryClient],
  );

  const notify = useCallback(
    (title: string, intent: "success" | "error" | "info" = "success") => {
      dispatchToast(
        <Toast>
          <ToastTitle>{title}</ToastTitle>
        </Toast>,
        { intent, timeout: 3500 },
      );
    },
    [dispatchToast],
  );

  const data: AppData | null = useMemo(() => {
    if (!me.data) return null;
    return {
      me: me.data,
      today: me.data.today,
      projects: projects.data ?? [],
      users: users.data ?? [],
      lists: lists.data ?? [],
      openTask: setTaskId,
      notify,
    };
  }, [me.data, projects.data, users.data, lists.data, notify]);

  if (!data) return null;

  if (route.page === "teams-config") {
    return (
      <AppContext.Provider value={data}>
        <ChannelConfig teams={teams} />
      </AppContext.Provider>
    );
  }

  const showSidebar = !inChannelTab;

  return (
    <AppContext.Provider value={data}>
      <div className={styles.shell}>
        {showSidebar && (
          <Sidebar
            route={route}
            open={!narrow || navOpen}
            overlay={narrow}
            onClose={() => setNavOpen(false)}
            onSettings={() => setSettingsOpen(true)}
            online={online}
          />
        )}
        <main className={styles.main} style={{ position: "relative" }}>
          {showSidebar && narrow && (
            <Button
              className={styles.menuButton}
              appearance="subtle"
              icon={<NavigationRegular />}
              aria-label="Navigation öffnen"
              onClick={() => setNavOpen(true)}
            />
          )}
          {route.page === "vorlagen" ? <TemplatesView /> : <TaskView route={route} compact={inChannelTab} />}
        </main>
        <TaskDrawer taskId={taskId} onClose={() => setTaskId(null)} overlay={drawerOverlay || inChannelTab} />
      </div>
      <SettingsDialog open={settingsOpen} onClose={() => setSettingsOpen(false)} inTeams={inTeams} />
      <Toaster toasterId={toasterId} position="bottom-end" />
    </AppContext.Provider>
  );
}
