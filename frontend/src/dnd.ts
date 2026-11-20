import type { DragEvent } from "react";
import type { Route } from "./route";
import type { Task } from "./types";

/** Ziehen und Ablegen von Aufgaben auf Einträge der Seitenleiste. */
export const DND_TYPE = "application/x-aufgabe";

export function startTaskDrag(e: DragEvent, task: Task) {
  e.dataTransfer.setData(DND_TYPE, String(task.id));
  e.dataTransfer.setData("text/plain", task.title);
  e.dataTransfer.effectAllowed = "move";
}

export function isTaskDrag(e: DragEvent) {
  return Array.from(e.dataTransfer.types).includes(DND_TYPE);
}

export function draggedTaskId(e: DragEvent): number | null {
  const value = e.dataTransfer.getData(DND_TYPE);
  return value ? Number(value) : null;
}

/** Was passiert, wenn eine Aufgabe auf diesem Ziel abgelegt wird – oder null, wenn das Ziel nichts annimmt. */
export function dropPatch(to: Route, today: string): { patch: Record<string, unknown>; label: string } | null {
  if (to.page !== "tasks") return null;
  switch (to.view) {
    case "mein-tag":
      return { patch: { myDay: true }, label: "Zu „Mein Tag“ hinzugefügt" };
    case "wichtig":
      return { patch: { priority: "hoch" }, label: "Als wichtig markiert" };
    case "geplant":
      return { patch: { dueDate: today }, label: "Für heute geplant" };
    case "aufgaben":
      return { patch: { listId: null }, label: "Aus der Liste genommen" };
    case "wiedervorlagen":
      return { patch: { kind: "wiedervorlage" }, label: "Als Wiedervorlage markiert" };
    case "liste":
      return to.id ? { patch: { listId: to.id }, label: "In die Liste verschoben" } : null;
    case "projekt":
      return to.id ? { patch: { projectId: to.id }, label: "Dem Projekt zugeordnet" } : null;
    default:
      return null;
  }
}
