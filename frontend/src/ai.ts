import { filesFrom } from "./attachments";
import type { AiInput, AiOption, AiQuestion, AiTaskDraft, TeamsDraft, UserRef } from "./types";

/** KI-Eingabe: reine Hilfsfunktionen für den Dialog „Mit KI erfassen“: testbar ohne Browser. */

/** Muss zu AiEndpoints.MaxImageBytes passen: Bild nach dem Verkleinern, damit Text und Bild unter 1 MB bleiben. */
export const MAX_AI_IMAGE_BYTES = 600 * 1024;

/** Größe für das Verkleinern: längste Seite höchstens maxSide, Seitenverhältnis bleibt, nie vergrößern. */
export function fitSize(width: number, height: number, maxSide: number): { width: number; height: number } {
  const scale = Math.min(1, maxSide / Math.max(width, height));
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

/** Bytes hinter einer Base64-Data-URL */
export function dataUrlBytes(url: string): number {
  const b64 = url.slice(url.indexOf(",") + 1);
  const padding = b64.endsWith("==") ? 2 : b64.endsWith("=") ? 1 : 0;
  return Math.floor((b64.length * 3) / 4) - padding;
}

/**
 * Bildschirmfoto verkleinern und als JPEG-Data-URL liefern. Erst groß und scharf (für die Texterkennung),
 * nur wenn nötig kleiner, bis es unter die Grenze passt.
 */
export async function imageToDataUrl(blob: Blob): Promise<string> {
  const bitmap = await createImageBitmap(blob);
  try {
    for (const [side, quality] of [
      [1800, 0.85],
      [1400, 0.8],
      [1100, 0.75],
      [900, 0.7],
    ] as const) {
      const { width, height } = fitSize(bitmap.width, bitmap.height, side);
      const canvas = document.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext("2d");
      if (!ctx) break;
      ctx.fillStyle = "#ffffff"; // transparente PNGs sonst schwarz
      ctx.fillRect(0, 0, width, height);
      ctx.drawImage(bitmap, 0, 0, width, height);
      const url = canvas.toDataURL("image/jpeg", quality);
      if (dataUrlBytes(url) <= MAX_AI_IMAGE_BYTES) return url;
    }
  } finally {
    bitmap.close();
  }
  throw new Error("Das Bild ist zu groß, bitte einen kleineren Ausschnitt einfügen.");
}

/** Inhalt der Zwischenablage für den KI-Dialog: erstes Bild und/oder Text; null, wenn nichts Brauchbares dabei ist. */
export function clipboardInput(data: DataTransfer | null): AiInput | null {
  if (!data) return null;
  const image = filesFrom(data).find((f) => /^image\/(png|jpeg|webp|gif|bmp)$/i.test(f.type));
  const text = data.getData("text/plain")?.trim() ?? "";
  if (!image && !text) return null;
  return { ...(text ? { text } : {}), ...(image ? { image } : {}) };
}

/** „zai-glm-latest“ → „GLM 5.3“ */
export function modelLabel(model: string): string {
  const m = model.trim().toLowerCase();
  if (m === "zai-glm-latest" || m === "zai-glm-5-3") return "GLM 5.3";
  return model || "KI";
}

/** Was während des Wartens dasteht, Nachdenken braucht oft 10 bis 60 Sekunden. */
export function thinkingText(seconds: number): string {
  if (seconds < 4) return "Die KI liest den Text …";
  if (seconds < 20) return `Die KI denkt nach … ${seconds} s`;
  if (seconds < 45) return `Gründliches Nachdenken dauert einen Moment … ${seconds} s`;
  return `Lange Texte brauchen etwas länger … ${seconds} s`;
}

/** „Anna, Thomas und Maria“ */
export function joinNames(names: string[]): string {
  if (names.length <= 1) return names[0] ?? "";
  return `${names.slice(0, -1).join(", ")} und ${names[names.length - 1]}`;
}

/** Meldung nach dem Anlegen */
export function createdMessage(count: number, notified: string[]): string {
  const head = count === 1 ? "Aufgabe angelegt" : `${count} Aufgaben angelegt`;
  if (notified.length === 0) return head;
  return `${head}, ${joinNames(notified)} ${notified.length === 1 ? "wurde" : "wurden"} benachrichtigt`;
}

/** Antwort auf eine Rückfrage übernehmen: Feld setzen, die Frage gilt als beantwortet. */
export function applyAnswer<T extends AiTaskDraft>(draft: T, question: AiQuestion, option: AiOption, users: UserRef[]): T {
  switch (question.field) {
    case "verantwortlich": {
      const id = Number(option.value);
      const user = users.find((u) => u.id === id) ?? { id, name: option.label, email: null, jobTitle: null };
      return { ...draft, assignee: user };
    }
    case "faellig":
      return { ...draft, dueDate: option.value };
    case "projekt":
      return { ...draft, project: { id: Number(option.value), label: option.label } };
  }
}

/** Welche Option gerade gewählt ist (passt zum Feldwert), für die Hervorhebung der Antwortknöpfe. */
export function currentOption(draft: AiTaskDraft, question: AiQuestion, meId: number): string | null {
  switch (question.field) {
    case "verantwortlich":
      return String(draft.assignee?.id ?? meId);
    case "faellig":
      return draft.dueDate;
    case "projekt":
      return draft.project ? String(draft.project.id) : null;
  }
}

/** Notizen der neuen Aufgabe: Zusammenfassung plus das Zitat, aus dem sie stammt. */
export function draftNotes(draft: AiTaskDraft, link?: string): string | undefined {
  const parts = [draft.notes?.trim(), draft.source ? `Aus: „${draft.source}“` : null, link ? `Teams-Nachricht: ${link}` : null].filter(Boolean);
  return parts.length ? parts.join("\n\n") : undefined;
}

/**
 * Text für den KI-Dialog aus „Als Aufgabe“: Wer die Nachricht geschrieben hat, hilft der KI bei
 * „kannst du …“: gemeint ist dann die Person, die die Nachricht als Aufgabe übernimmt.
 */
export function teamsDraftText(draft: TeamsDraft, formatDate: (iso: string) => string = defaultDate): string {
  const text = draft.text.trim();
  if (!draft.from && !draft.sentAt) return text;
  const head = ["Teams-Nachricht", draft.from ? `von ${draft.from}` : null, draft.sentAt ? `(${formatDate(draft.sentAt)})` : null].filter(Boolean).join(" ");
  return text ? `${head}:\n${text}` : "";
}

/** Titel ohne KI: erste Zeile, die keine Anrede ist („Hallo Anna,“), höchstens 120 Zeichen. */
export function plainTitle(text: string): string {
  const lines = text.split("\n").map((l) => l.trim()).filter(Boolean);
  const greeting = /^(hallo|hi|hey|moin|servus|guten (morgen|tag|abend)|liebe[rs]?|sehr geehrte[rs]?)\b[^.!?]{0,40}[,!]?$/i;
  const line = lines.find((l) => !greeting.test(l)) ?? lines[0] ?? "";
  return line.length > 120 ? `${line.slice(0, 117)}…` : line;
}

function defaultDate(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString("de-DE", { dateStyle: "medium", timeStyle: "short" });
}
