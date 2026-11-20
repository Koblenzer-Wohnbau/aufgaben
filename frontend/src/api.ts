import type {
  AuthConfig, Counts, CreateTask, Me, Project, Task, TaskDetail, TaskEvent, TaskList, Template, UserRef, ViewKey,
} from "./types";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

/** In Teams liefert diese Funktion das SSO-Token; im Browser bleibt sie leer (Cookie-Sitzung). */
let tokenProvider: (() => Promise<string>) | null = null;

export function setTokenProvider(fn: (() => Promise<string>) | null) {
  tokenProvider = fn;
}

export async function authHeaders(): Promise<Record<string, string>> {
  return tokenProvider ? { Authorization: `Bearer ${await tokenProvider()}` } : {};
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {
    "X-Aufgaben-Request": "1",
    ...(await authHeaders()),
  };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const res = await fetch(path, {
    method,
    headers,
    credentials: "same-origin",
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    let message = res.status === 401 ? "Bitte anmelden." : `Fehler ${res.status}`;
    try {
      const data = await res.json();
      if (data?.error) message = data.error;
    } catch {
      /* keine JSON-Antwort */
    }
    throw new ApiError(res.status, message);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export const api = {
  authConfig: () => request<AuthConfig>("GET", "/api/auth/config"),
  me: () => request<Me>("GET", "/api/me"),
  updateMe: (body: Partial<Pick<Me, "notifyChannel" | "digestEnabled">>) => request<Me>("PATCH", "/api/me", body),
  createCalendarFeed: () => request<{ url: string }>("POST", "/api/me/calendar-feed"),
  deleteCalendarFeed: () => request<void>("DELETE", "/api/me/calendar-feed"),
  logout: () => request<void>("POST", "/auth/logout"),

  counts: () => request<Counts>("GET", "/api/counts"),
  tasks: (view: ViewKey, id?: number, q?: string) => {
    const p = new URLSearchParams({ view });
    if (id !== undefined) p.set("id", String(id));
    if (q) p.set("q", q);
    return request<Task[]>("GET", `/api/tasks?${p}`);
  },
  task: (id: number) => request<TaskDetail>("GET", `/api/tasks/${id}`),
  createTask: (body: CreateTask) => request<Task>("POST", "/api/tasks", body),
  patchTask: (id: number, body: Record<string, unknown>) => request<Task>("PATCH", `/api/tasks/${id}`, body),
  completeTask: (id: number) => request<Task>("POST", `/api/tasks/${id}/complete`),
  reopenTask: (id: number) => request<Task>("POST", `/api/tasks/${id}/reopen`),
  deleteTask: (id: number) => request<void>("DELETE", `/api/tasks/${id}`),
  addChecklist: (id: number, title: string) => request<Task>("POST", `/api/tasks/${id}/checklist`, { title }),
  updateChecklist: (id: number, itemId: number, body: { title?: string; done?: boolean }) =>
    request<Task>("PATCH", `/api/tasks/${id}/checklist/${itemId}`, body),
  deleteChecklist: (id: number, itemId: number) => request<Task>("DELETE", `/api/tasks/${id}/checklist/${itemId}`),
  comment: (id: number, text: string) => request<TaskEvent>("POST", `/api/tasks/${id}/comments`, { text }),

  users: (q?: string) => request<UserRef[]>("GET", q ? `/api/users?q=${encodeURIComponent(q)}` : "/api/users"),

  lists: () => request<TaskList[]>("GET", "/api/lists"),
  createList: (name: string, color = "blau") => request<TaskList>("POST", "/api/lists", { name, color }),
  updateList: (id: number, body: { name?: string; color?: string }) => request<TaskList>("PATCH", `/api/lists/${id}`, body),
  deleteList: (id: number) => request<void>("DELETE", `/api/lists/${id}`),

  projects: () => request<Project[]>("GET", "/api/projects"),
  createProject: (body: { number?: string; name: string; teamName?: string }) => request<Project>("POST", "/api/projects", body),
  linkChannel: (body: { channelId: string; teamId?: string; teamName?: string; channelName?: string; projectId?: number }) =>
    request<Project>("POST", "/api/projects/channel", body),

  templates: () => request<Template[]>("GET", "/api/templates"),
  applyTemplate: (id: number, body: { startDate: string; projectId?: number | null; defaultAssigneeId?: number | null; assignees?: Record<number, number>; titleSuffix?: string }) =>
    request<{ created: number; taskIds: number[] }>("POST", `/api/templates/${id}/apply`, body),
};
