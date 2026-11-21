import { useEffect, useState } from "react";
import type { ViewKey } from "./types";

export type Route =
  | { page: "tasks"; view: ViewKey; id?: number; q?: string; taskId?: number }
  | { page: "vorlagen" }
  | { page: "verwaltung" }
  | { page: "teams-config" }
  /** Fenster in Teams nach „Als Aufgabe“ an einer Nachricht */
  | { page: "teams-ki"; draft: string };

const SIMPLE_VIEWS: ViewKey[] = ["mein-tag", "aufgaben", "wichtig", "geplant", "zugewiesen", "delegiert", "wiedervorlagen", "warteschleife", "erledigt", "papierkorb"];

export function parseHash(hash: string): Route {
  const parts = hash.replace(/^#\/?/, "").split("/").filter(Boolean).map(decodeURIComponent);
  const [first, second] = parts;
  if (!first) return { page: "tasks", view: "mein-tag" };
  if (first === "vorlagen") return { page: "vorlagen" };
  if (first === "verwaltung") return { page: "verwaltung" };
  if (first === "teams" && second === "config") return { page: "teams-config" };
  if (first === "teams" && second === "ki" && parts[2]) return { page: "teams-ki", draft: parts[2] };
  if (first === "aufgabe" && second) return { page: "tasks", view: "mein-tag", taskId: Number(second) };
  if ((first === "liste" || first === "projekt" || first === "person" || first === "vertretung") && second) return { page: "tasks", view: first, id: Number(second) };
  if (first === "suche") return { page: "tasks", view: "suche", q: second ?? "" };
  if ((SIMPLE_VIEWS as string[]).includes(first)) return { page: "tasks", view: first as ViewKey };
  return { page: "tasks", view: "mein-tag" };
}

export function hrefFor(route: Route): string {
  if (route.page === "vorlagen") return "#/vorlagen";
  if (route.page === "verwaltung") return "#/verwaltung";
  if (route.page === "teams-config") return "#/teams/config";
  if (route.page === "teams-ki") return `#/teams/ki/${encodeURIComponent(route.draft)}`;
  if (route.view === "liste" || route.view === "projekt" || route.view === "person" || route.view === "vertretung") return `#/${route.view}/${route.id}`;
  if (route.view === "suche") return `#/suche/${encodeURIComponent(route.q ?? "")}`;
  return route.view === "mein-tag" ? "#/" : `#/${route.view}`;
}

export function navigate(route: Route) {
  window.location.hash = hrefFor(route);
}

export function useRoute(): Route {
  const [route, setRoute] = useState(() => parseHash(window.location.hash));
  useEffect(() => {
    const handler = () => setRoute(parseHash(window.location.hash));
    window.addEventListener("hashchange", handler);
    return () => window.removeEventListener("hashchange", handler);
  }, []);
  return route;
}
