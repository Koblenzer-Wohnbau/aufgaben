import { createContext, useContext } from "react";
import type { Me, Project, TaskList, UserRef } from "./types";

export interface AppData {
  me: Me;
  today: string;
  projects: Project[];
  users: UserRef[];
  lists: TaskList[];
  openTask: (id: number | null) => void;
  notify: (title: string, intent?: "success" | "error" | "info") => void;
}

export const AppContext = createContext<AppData | null>(null);

export function useApp(): AppData {
  const ctx = useContext(AppContext);
  if (!ctx) throw new Error("AppContext fehlt");
  return ctx;
}

/** Projekt zu einer Eingabe nach "#" finden: Nummer oder Namensanfang. */
export function matchProject(projects: Project[], query: string): Project | undefined {
  const q = query.toLowerCase();
  return (
    projects.find((p) => p.number?.toLowerCase() === q) ??
    projects.find((p) => p.number?.toLowerCase().startsWith(q)) ??
    projects.find((p) => p.name.toLowerCase().startsWith(q)) ??
    projects.find((p) => p.label.toLowerCase().includes(q))
  );
}

/** Person zu einer Eingabe nach "@" finden: Vor- oder Nachname, E-Mail-Anfang. */
export function matchUser(users: UserRef[], query: string): UserRef | undefined {
  const q = query.toLowerCase();
  return (
    users.find((u) => u.name.toLowerCase().split(/\s+/).some((part) => part === q)) ??
    users.find((u) => u.name.toLowerCase().split(/\s+/).some((part) => part.startsWith(q))) ??
    users.find((u) => u.email?.toLowerCase().startsWith(q))
  );
}

export function initials(name: string): string {
  const parts = name.split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? "") + (parts.length > 1 ? parts[parts.length - 1][0] : "")).toUpperCase();
}
