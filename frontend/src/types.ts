export interface UserRef {
  id: number;
  name: string;
  email: string | null;
  jobTitle: string | null;
}

export interface ProjectRef {
  id: number;
  label: string;
}

export interface Project {
  id: number;
  label: string;
  number: string | null;
  name: string;
  teamName: string | null;
  archived: boolean;
  linkedToTeams: boolean;
  openCount: number;
}

export interface ChecklistItem {
  id: number;
  title: string;
  done: boolean;
}

export type Priority = "niedrig" | "normal" | "hoch";
export type Kind = "aufgabe" | "wiedervorlage";
export type RecurrenceRule = "" | "taeglich" | "werktags" | "woechentlich" | "monatlich" | "jaehrlich";

export interface Task {
  id: number;
  title: string;
  notes: string;
  status: "offen" | "erledigt";
  kind: Kind;
  priority: Priority;
  createdBy: UserRef;
  assignee: UserRef;
  listId: number | null;
  project: ProjectRef | null;
  dueDate: string | null;
  dueTime: string | null;
  remindAt: string | null;
  myDay: boolean;
  recurrence: RecurrenceRule;
  completedAt: string | null;
  completedBy: UserRef | null;
  checklist: ChecklistItem[];
  source: string;
  createdAt: string;
  updatedAt: string;
  version: number;
  isDelegated: boolean;
  isOverdue: boolean;
  canDelete: boolean;
}

export interface TaskEvent {
  id: number;
  kind: string;
  text: string;
  actor: UserRef | null;
  createdAt: string;
}

export interface TaskDetail {
  task: Task;
  events: TaskEvent[];
}

export interface Counts {
  myDay: number;
  important: number;
  planned: number;
  assigned: number;
  delegated: number;
  followUps: number;
  overdue: number;
  lists: Record<string, number>;
  projects: Record<string, number>;
}

export interface TaskList {
  id: number;
  name: string;
  color: string;
  position: number;
  openCount: number;
}

export interface Me {
  id: number;
  name: string;
  email: string | null;
  isAdmin: boolean;
  notifyChannel: "auto" | "teams" | "email" | "aus";
  digestEnabled: boolean;
  teamsConnected: boolean;
  hasCalendarFeed: boolean;
  mailAvailable: boolean;
  teamsNotificationsAvailable: boolean;
  today: string;
}

export interface DevUser {
  oid: string;
  name: string;
  jobTitle: string | null;
  isAdmin: boolean;
}

export interface AuthConfig {
  mode: "entra" | "dev";
  devUsers: DevUser[] | null;
}

export interface TemplateStep {
  id: number;
  position: number;
  title: string;
  notes: string;
  offsetDays: number;
  priority: Priority;
  checklist: string[];
  roleHint: string;
}

export interface Template {
  id: number;
  name: string;
  description: string;
  steps: TemplateStep[];
}

export interface CreateTask {
  title: string;
  notes?: string;
  priority?: Priority;
  kind?: Kind;
  assigneeId?: number | null;
  listId?: number | null;
  projectId?: number | null;
  dueDate?: string | null;
  dueTime?: string | null;
  remindAt?: string | null;
  myDay?: boolean;
  recurrence?: RecurrenceRule;
  checklist?: string[];
}

export type ViewKey =
  | "mein-tag"
  | "wichtig"
  | "geplant"
  | "zugewiesen"
  | "delegiert"
  | "wiedervorlagen"
  | "erledigt"
  | "liste"
  | "projekt"
  | "suche";
