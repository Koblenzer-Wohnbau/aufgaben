import { StrictMode, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  FluentProvider,
  Spinner,
  teamsDarkTheme,
  teamsHighContrastTheme,
  teamsLightTheme,
} from "@fluentui/react-components";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App } from "./App";
import { setTokenProvider } from "./api";
import { inTeams, initTeams, onTeamsThemeChange, teamsToken, type TeamsInfo, type TeamsTheme } from "./teams";
import "./global.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, refetchOnWindowFocus: true, retry: 1 },
  },
});

function themeFor(t: TeamsTheme) {
  if (t === "dark") return teamsDarkTheme;
  if (t === "contrast") return teamsHighContrastTheme;
  return teamsLightTheme;
}

function browserTheme(): TeamsTheme {
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "default";
}

function Root() {
  const [theme, setTheme] = useState<TeamsTheme>(inTeams ? "default" : browserTheme());
  const [teams, setTeams] = useState<TeamsInfo | null>(null);
  const [ready, setReady] = useState(!inTeams);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!inTeams) {
      const mq = window.matchMedia("(prefers-color-scheme: dark)");
      const handler = () => setTheme(browserTheme());
      mq.addEventListener("change", handler);
      return () => mq.removeEventListener("change", handler);
    }
    initTeams()
      .then((info) => {
        setTeams(info);
        setTheme(info.theme);
        onTeamsThemeChange(setTheme);
        setTokenProvider(teamsToken);
        setReady(true);
      })
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
    return undefined;
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  return (
    <FluentProvider theme={themeFor(theme)} style={{ height: "100%", background: "transparent" }}>
      <QueryClientProvider client={queryClient}>
        {error ? (
          <div className="center-screen">Teams-Verbindung fehlgeschlagen: {error}</div>
        ) : ready ? (
          <App teams={teams} />
        ) : (
          <div className="center-screen">
            <Spinner label="Verbinde mit Teams …" />
          </div>
        )}
      </QueryClientProvider>
    </FluentProvider>
  );
}

// Installierbare App (PWA) – nicht in Teams, dort lädt Teams die Seite selbst.
if (!inTeams && "serviceWorker" in navigator && window.isSecureContext && import.meta.env.PROD) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch(() => {
      /* ohne Service Worker funktioniert die App genauso, nur ohne Installation */
    });
  });
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
