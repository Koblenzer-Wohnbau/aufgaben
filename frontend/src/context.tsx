import { createContext, useContext } from "react";
import type { ConfirmRequest } from "./components/ConfirmDialog";
import type { AiInput, Me, Project, TaskList, UserRef } from "./types";

export interface AppData {
  me: Me;
  today: string;
  projects: Project[];
  users: UserRef[];
  lists: TaskList[];
  openTask: (id: number | null) => void;
  selectedTaskId: number | null;
  notify: (title: string, intent?: "success" | "error" | "info") => void;
  /** KI-Dialog öffnen, optional schon mit Text oder Bildschirmfoto */
  openAi: (input?: AiInput) => void;
  /** Rückfrage als Dialog – statt window.confirm, das Teams in Tabs blockiert. true = bestätigt. */
  confirm: (request: ConfirmRequest) => Promise<boolean>;
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

/**
 * Kürzel für die Schnelleingabe, das genau diese Person trifft: Nachname, Vorname oder der
 * Anfang der E-Mail-Adresse – je nachdem, was eindeutig ist (z. B. bei gleichen Nachnamen).
 */
export function mentionFor(user: UserRef, users: UserRef[]): string {
  const parts = user.name.split(/\s+/).filter(Boolean);
  const local = user.email?.split("@")[0];
  const candidates = [parts.at(-1), parts[0], local].filter(
    (c): c is string => !!c && /^[\p{L}\p{N}][\p{L}\p{N}._-]*$/u.test(c),
  );
  return candidates.find((c) => matchUser(users, c)?.id === user.id) ?? candidates[0] ?? user.name;
}

export function initials(name: string): string {
  const parts = name.split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? "") + (parts.length > 1 ? parts[parts.length - 1][0] : "")).toUpperCase();
}
