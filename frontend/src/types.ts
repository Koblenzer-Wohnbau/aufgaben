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

export interface Attachment {
  id: number;
  fileName: string;
  contentType: string;
  size: number;
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
  attachments: Attachment[];
  /** „Warte auf …“ – leer, wenn auf niemanden gewartet wird */
  waitingFor: string;
  waitingSince: string | null;
  /** Zurückgestellt bis (bei „Warte auf …“: Tag zum Nachhaken) */
  snoozedUntil: string | null;
  /** Gerade ausgeblendet – eine Fälligkeit holt die Aufgabe früher zurück */
  isSnoozed: boolean;
  /** Im Papierkorb seit … (nur in der Ansicht „Papierkorb“ gesetzt) */
  deletedAt: string | null;
  deletedBy: UserRef | null;
}

export interface AiOption {
  value: string;
  label: string;
}

/** Rückfrage der KI zu einem unsicheren Feld */
export interface AiQuestion {
  field: "verantwortlich" | "faellig" | "projekt";
  text: string;
  options: AiOption[];
}

/** Vorgeschlagene Aufgabe – noch nicht angelegt. assignee null: bei der eingebenden Person. */
export interface AiTaskDraft {
  title: string;
  notes: string | null;
  dueDate: string | null;
  dueTime: string | null;
  priority: Priority;
  kind: Kind;
  assignee: UserRef | null;
  project: ProjectRef | null;
  checklist: string[];
  source: string | null;
  questions: AiQuestion[];
}

export interface AiSuggestion {
  tasks: AiTaskDraft[];
  note: string | null;
  model: string;
}

/** Was in den KI-Dialog übernommen wird: Text und/oder ein Bildschirmfoto */
export interface AiInput {
  text?: string;
  image?: Blob;
}

export type BulkAction = "erledigen" | "loeschen" | "wiederherstellen" | "endgueltig-loeschen";

export interface BulkResult {
  /** ausgeführt */
  done: number;
  /** übersprungen: nicht erlaubt oder nicht mehr vorhanden */
  skipped: number;
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
  /** false: nur über eine Lese-Freigabe sichtbar */
  canEdit: boolean;
  /** true: über ein Einsichtsrecht sichtbar, nicht eigene Aufgabe */
  viaGrant: boolean;
}

export interface Counts {
  inbox: number;
  myDay: number;
  important: number;
  planned: number;
  assigned: number;
  delegated: number;
  followUps: number;
  overdue: number;
  lists: Record<string, number>;
  projects: Record<string, number>;
  /** Warteschleife: „Warte auf …“ und Zurückgestelltes */
  waiting: number;
  /** davon: Tag zum Nachhaken ist da */
  waitingDue: number;
  /** Aufgaben im eigenen Papierkorb */
  trash: number;
}

export interface TaskList {
  id: number;
  name: string;
  color: string;
  position: number;
  openCount: number;
  isOwner: boolean;
  owner: UserRef;
  members: UserRef[];
}

export interface AccessGrant {
  id: number;
  owner: UserRef;
  viewer: UserRef;
  permission: "lesen" | "bearbeiten";
  validFrom: string;
  validUntil: string | null;
  purpose: string;
  status: "entwurf" | "aktiv" | "widerrufen";
  isCurrentlyActive: boolean;
  createdAt: string;
  confirmedAt: string | null;
  consentNote: string;
  revokedAt: string | null;
  revokeReason: string;
  accessCount: number;
}

export interface AccessLogEntry {
  id: number;
  action: string;
  viewer: UserRef;
  owner: UserRef;
  taskId: number | null;
  at: string;
}

export interface MyAccess {
  grantId: number;
  owner: UserRef;
  permission: "lesen" | "bearbeiten";
  validUntil: string | null;
}

export interface AdminStatus {
  graphConfigured: boolean;
  mailConfigured: boolean;
  teamsNotificationsConfigured: boolean;
  activeUsers: number;
  pendingNotifications: number;
  failedNotifications: number;
  lastDirectorySync: string | null;
  lastDirectorySyncResult: string | null;
  mailInboxConfigured: boolean;
  lastMailInboxRun: string | null;
  lastMailInboxResult: string | null;
  rejectedMailsLast7Days: number;
  version: string;
}

export interface Suggestions {
  yesterday: Task[];
  dueSoon: Task[];
  important: Task[];
  recent: Task[];
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
  mailInboxAddress: string | null;
  digestOnWeekends: boolean;
  /** KI-Vorschläge (Mistral) auf diesem Server eingeschaltet */
  aiAvailable: boolean;
}

/** Eigener Urlaub mit Vertretung (from/until: erster und letzter freier Tag). */
export interface Absence {
  id: number;
  from: string;
  until: string;
  /** erster Arbeitstag danach */
  returnDay: string;
  deputy: UserRef;
  started: boolean;
  /** vorbei oder vorzeitig beendet */
  ended: boolean;
  /** gerade an die Vertretung übergeben */
  handedOver: number;
  /** behalten und bis nach dem Urlaub zurückgestellt */
  kept: number;
}

/** Ich vertrete gerade jemanden. open: übergebene Aufgaben, die noch offen sind. */
export interface DeputyInfo {
  absenceId: number;
  person: UserRef;
  from: string;
  until: string;
  open: number;
}

export interface AbsenceStatus {
  /** eingetragener oder laufender eigener Urlaub */
  current: Absence | null;
  /** zurück – „Was ist passiert?“ ist noch nicht geschlossen */
  returned: Absence | null;
  deputy: DeputyInfo[];
}

export type AbsenceDecision = "uebergeben" | "behalten";

export interface AbsenceItem {
  taskId: number;
  title: string;
  kind: Kind;
  dueDate: string | null;
  waitingFor: string;
  /** bei „Warte auf …“: Tag zum Nachhaken */
  followUp: string | null;
  decision: AbsenceDecision | null;
  assignee: UserRef;
  canOpen: boolean;
}

export interface AbsencePlan {
  absence: Absence;
  items: AbsenceItem[];
}

export interface HandoverState {
  taskId: number;
  title: string;
  status: "offen" | "erledigt";
  assignee: UserRef;
  completedBy: UserRef | null;
  completedAt: string | null;
  canTakeBack: boolean;
  canOpen: boolean;
}

export interface SummaryEvent {
  id: number;
  taskId: number;
  taskTitle: string;
  kind: string;
  text: string;
  actor: UserRef | null;
  createdAt: string;
}

export interface AbsenceSummary {
  absence: Absence;
  handovers: HandoverState[];
  kept: number;
  events: SummaryEvent[];
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
  /** aus einem bestätigten KI-Vorschlag */
  fromAi?: boolean;
}

export type ViewKey =
  | "mein-tag"
  | "aufgaben"
  | "person"
  | "wichtig"
  | "geplant"
  | "zugewiesen"
  | "delegiert"
  | "wiedervorlagen"
  | "warteschleife"
  | "erledigt"
  | "papierkorb"
  | "vertretung"
  | "liste"
  | "projekt"
  | "suche";
