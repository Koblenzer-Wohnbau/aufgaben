import type {
  AccessGrant, AccessLogEntry, AdminStatus, Attachment, AuthConfig, BulkAction, BulkResult, Counts, MyAccess, CreateTask, Me, Project, Suggestions, Task, TaskDetail, TaskEvent, TaskList, Template, UserRef, ViewKey,
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

/** Datei vom Server laden und im Browser speichern (Name aus Content-Disposition). */
async function download(path: string): Promise<string> {
  const res = await fetch(path, {
    headers: { "X-Aufgaben-Request": "1", ...(await authHeaders()) },
    credentials: "same-origin",
  });
  if (!res.ok) {
    let message = `Fehler ${res.status}`;
    try {
      const data = await res.json();
      if (data?.error) message = data.error;
    } catch {
      /* keine JSON-Antwort */
    }
    throw new ApiError(res.status, message);
  }
  const disposition = res.headers.get("Content-Disposition") ?? "";
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? "download";
  const url = URL.createObjectURL(await res.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = decodeURIComponent(name);
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
  return link.download;
}

export const api = {
  authConfig: () => request<AuthConfig>("GET", "/api/auth/config"),
  me: () => request<Me>("GET", "/api/me"),
  updateMe: (body: Partial<Pick<Me, "notifyChannel" | "digestEnabled">>) => request<Me>("PATCH", "/api/me", body),
  createCalendarFeed: () => request<{ url: string }>("POST", "/api/me/calendar-feed"),
  deleteCalendarFeed: () => request<void>("DELETE", "/api/me/calendar-feed"),
  logout: () => request<void>("POST", "/auth/logout"),

  counts: () => request<Counts>("GET", "/api/counts"),
  suggestions: () => request<Suggestions>("GET", "/api/suggestions"),
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
  /** In den Papierkorb legen */
  deleteTask: (id: number) => request<void>("DELETE", `/api/tasks/${id}`),
  restoreTask: (id: number) => request<Task>("POST", `/api/tasks/${id}/restore`),
  purgeTask: (id: number) => request<void>("DELETE", `/api/tasks/${id}/endgueltig`),
  bulkTasks: (action: BulkAction, ids: number[]) => request<BulkResult>("POST", "/api/tasks/bulk", { action, ids }),
  deleteCompleted: () => request<BulkResult>("POST", "/api/tasks/erledigte-loeschen"),
  emptyTrash: () => request<BulkResult>("POST", "/api/papierkorb/leeren"),
  addChecklist: (id: number, title: string) => request<Task>("POST", `/api/tasks/${id}/checklist`, { title }),
  updateChecklist: (id: number, itemId: number, body: { title?: string; done?: boolean }) =>
    request<Task>("PATCH", `/api/tasks/${id}/checklist/${itemId}`, body),
  deleteChecklist: (id: number, itemId: number) => request<Task>("DELETE", `/api/tasks/${id}/checklist/${itemId}`),
  downloadAttachment: async (taskId: number, attachmentId: number, fileName: string) => {
    const res = await fetch(`/api/tasks/${taskId}/attachments/${attachmentId}`, {
      headers: await authHeaders(),
      credentials: "same-origin",
    });
    if (!res.ok) throw new ApiError(res.status, "Anhang konnte nicht geladen werden.");
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  },
  uploadAttachment: async (taskId: number, file: Blob, fileName: string): Promise<Attachment> => {
    const res = await fetch(`/api/tasks/${taskId}/attachments?name=${encodeURIComponent(fileName)}`, {
      method: "POST",
      headers: {
        "X-Aufgaben-Request": "1",
        "Content-Type": file.type || "application/octet-stream",
        ...(await authHeaders()),
      },
      credentials: "same-origin",
      body: file,
    });
    if (!res.ok) {
      // 413 ohne JSON kommt von Nginx: dessen Grenze für Uploads ist noch nicht eingerichtet
      let message = res.status === 413
        ? "Die Datei ist zu groß für den Server. Höchstens 10 MB – sonst die Nginx-Einrichtung aktualisieren."
        : `Hochladen fehlgeschlagen (${res.status}).`;
      try {
        const data = await res.json();
        if (data?.error) message = data.error;
      } catch {
        /* keine JSON-Antwort */
      }
      throw new ApiError(res.status, message);
    }
    return (await res.json()) as Attachment;
  },
  deleteAttachment: (taskId: number, attachmentId: number) =>
    request<Task>("DELETE", `/api/tasks/${taskId}/attachments/${attachmentId}`),
  /** Bildvorschau mit Anmeldung laden (in Teams per Token) und als Objekt-URL zurückgeben. */
  attachmentPreview: async (taskId: number, attachmentId: number): Promise<string> => {
    const res = await fetch(`/api/tasks/${taskId}/attachments/${attachmentId}/vorschau`, {
      headers: await authHeaders(),
      credentials: "same-origin",
    });
    if (!res.ok) throw new ApiError(res.status, "Vorschau nicht verfügbar.");
    return URL.createObjectURL(await res.blob());
  },
  comment: (id: number, text: string) => request<TaskEvent>("POST", `/api/tasks/${id}/comments`, { text }),

  users: (q?: string) => request<UserRef[]>("GET", q ? `/api/users?q=${encodeURIComponent(q)}` : "/api/users"),

  lists: () => request<TaskList[]>("GET", "/api/lists"),
  createList: (name: string, color = "blau") => request<TaskList>("POST", "/api/lists", { name, color }),
  updateList: (id: number, body: { name?: string; color?: string }) => request<TaskList>("PATCH", `/api/lists/${id}`, body),
  deleteList: (id: number) => request<void>("DELETE", `/api/lists/${id}`),
  addListMember: (id: number, userId: number) => request<void>("POST", `/api/lists/${id}/members`, { userId }),
  removeListMember: (id: number, userId: number) => request<void>("DELETE", `/api/lists/${id}/members/${userId}`),

  projects: () => request<Project[]>("GET", "/api/projects"),
  createProject: (body: { number?: string; name: string; teamName?: string }) => request<Project>("POST", "/api/projects", body),
  linkChannel: (body: { channelId: string; teamId?: string; teamName?: string; channelName?: string; projectId?: number }) =>
    request<Project>("POST", "/api/projects/channel", body),

  accessMine: () => request<MyAccess[]>("GET", "/api/access/mine"),
  accessAboutMe: () => request<{ grants: AccessGrant[]; log: AccessLogEntry[] }>("GET", "/api/access/about-me"),
  adminAccess: () => request<AccessGrant[]>("GET", "/api/admin/access"),
  createAccess: (body: { ownerId: number; viewerId: number; permission: string; validFrom: string; validUntil: string | null; purpose: string }) =>
    request<AccessGrant>("POST", "/api/admin/access", body),
  confirmAccess: (id: number, note: string) => request<AccessGrant>("POST", `/api/admin/access/${id}/confirm`, { note }),
  revokeAccess: (id: number, note: string) => request<AccessGrant>("POST", `/api/admin/access/${id}/revoke`, { note }),
  deleteAccess: (id: number) => request<void>("DELETE", `/api/admin/access/${id}`),
  accessLog: (id: number) => request<AccessLogEntry[]>("GET", `/api/admin/access/${id}/log`),
  adminStatus: () => request<AdminStatus>("GET", "/api/admin/status"),
  directorySync: () => request<{ added: number; updated: number; deactivated: number }>("POST", "/api/admin/directory-sync"),
  pollInbox: () => request<{ created: number; result: string }>("POST", "/api/admin/mail-inbox/poll"),
  downloadTeamsPackage: () => download("/api/admin/teams-package"),

  templates: () => request<Template[]>("GET", "/api/templates"),
  applyTemplate: (id: number, body: { startDate: string; projectId?: number | null; defaultAssigneeId?: number | null; assignees?: Record<number, number>; titleSuffix?: string }) =>
    request<{ created: number; taskIds: number[] }>("POST", `/api/templates/${id}/apply`, body),
};
