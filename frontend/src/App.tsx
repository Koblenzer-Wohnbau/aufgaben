import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from "react";
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
import { clipboardInput } from "./ai";
import { api, ApiError } from "./api";
import { AppContext, type AppData } from "./context";
import type { AiInput } from "./types";
import { connectLive } from "./realtime";
import { useRoute } from "./route";
import { inChannelTab, inTeams, type TeamsInfo } from "./teams";
import { LoginScreen } from "./components/LoginScreen";
import { Sidebar } from "./components/Sidebar";
import { TaskView } from "./components/TaskView";
import { TrashView } from "./components/TrashView";
import { TaskDrawer } from "./components/TaskDrawer";

// Selten genutzte Bereiche erst bei Bedarf laden – kleinerer Start-Download.
const TemplatesView = lazy(() => import("./components/TemplatesView").then((m) => ({ default: m.TemplatesView })));
const SettingsDialog = lazy(() => import("./components/SettingsDialog").then((m) => ({ default: m.SettingsDialog })));
const ChannelConfig = lazy(() => import("./components/ChannelConfig").then((m) => ({ default: m.ChannelConfig })));
const AdminView = lazy(() => import("./components/AdminView").then((m) => ({ default: m.AdminView })));
const AiCaptureDialog = lazy(() => import("./components/AiCaptureDialog").then((m) => ({ default: m.AiCaptureDialog })));

function isTyping(target: EventTarget | null) {
  const el = target as HTMLElement | null;
  return !!el && (el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.tagName === "SELECT" || el.isContentEditable);
}

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
          if (m.type === "lists") {
            void queryClient.invalidateQueries({ queryKey: ["lists"] });
            void queryClient.invalidateQueries({ queryKey: ["counts"] });
            void queryClient.invalidateQueries({ queryKey: ["tasks"] });
          }
          if (m.type === "verbunden") {
            void queryClient.invalidateQueries();
          }
        },
        setOnline,
      ),
    [queryClient],
  );

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      if (e.key === "Escape" && !isTyping(e.target)) {
        setTaskId(null);
        return;
      }
      if (isTyping(e.target)) return;
      if (e.key === "n") {
        const input = document.querySelector<HTMLInputElement>('input[aria-label="Neue Aufgabe"]');
        if (input) {
          e.preventDefault();
          input.focus();
        }
      } else if (e.key === "/") {
        const search = document.querySelector<HTMLInputElement>('input[aria-label="Suchen"]');
        if (search) {
          e.preventDefault();
          search.focus();
        }
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

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

  // KI-Eingabe: Strg+V irgendwo auf der Seite (nicht in einem Eingabefeld oder Dialog) öffnet den Dialog
  // gleich mit dem eingefügten Text oder Bildschirmfoto – etwa einer kopierten Teams-Nachricht.
  const [aiInput, setAiInput] = useState<AiInput | null>(null);
  const openAi = useCallback((input?: AiInput) => setAiInput(input ?? {}), []);
  const aiAvailable = me.data?.aiAvailable ?? false;
  useEffect(() => {
    if (!aiAvailable) return;
    const onPaste = (e: ClipboardEvent) => {
      if (e.defaultPrevented || isTyping(e.target)) return;
      const target = e.target as HTMLElement | null;
      if (target?.closest('[role="dialog"], [role="alertdialog"], .fui-InlineDrawer, .fui-OverlayDrawer, .fui-DrawerBody')) return;
      const input = clipboardInput(e.clipboardData);
      if (!input) return;
      e.preventDefault();
      setAiInput(input);
    };
    window.addEventListener("paste", onPaste);
    return () => window.removeEventListener("paste", onPaste);
  }, [aiAvailable]);

  const data: AppData | null = useMemo(() => {
    if (!me.data) return null;
    return {
      me: me.data,
      today: me.data.today,
      projects: projects.data ?? [],
      users: users.data ?? [],
      lists: lists.data ?? [],
      openTask: setTaskId,
      selectedTaskId: taskId,
      notify,
      openAi,
    };
  }, [me.data, projects.data, users.data, lists.data, notify, taskId, openAi]);

  if (!data) return null;

  if (route.page === "teams-config") {
    return (
      <AppContext.Provider value={data}>
        <Suspense fallback={<Spinner />}>
          <ChannelConfig teams={teams} />
        </Suspense>
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
          <Suspense fallback={<Spinner style={{ marginTop: 48 }} />}>
            {route.page === "vorlagen" ? (
              <TemplatesView />
            ) : route.page === "verwaltung" ? (
              <AdminView />
            ) : route.view === "papierkorb" ? (
              <TrashView compact={inChannelTab} />
            ) : (
              <TaskView route={route} compact={inChannelTab} />
            )}
          </Suspense>
        </main>
        <TaskDrawer taskId={taskId} onClose={() => setTaskId(null)} overlay={drawerOverlay || inChannelTab} />
      </div>
      {settingsOpen && (
        <Suspense fallback={null}>
          <SettingsDialog open={settingsOpen} onClose={() => setSettingsOpen(false)} inTeams={inTeams} />
        </Suspense>
      )}
      {aiInput && (
        <Suspense fallback={null}>
          <AiCaptureDialog initial={aiInput} onClose={() => setAiInput(null)} />
        </Suspense>
      )}
      <Toaster toasterId={toasterId} position="bottom-end" />
    </AppContext.Provider>
  );
}
