/** Regeln für selbst hochgeladene Anhänge – müssen zu AttachmentRules im Backend passen. */
export const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

/** Nur diese Typen bekommen eine Vorschau; der Server prüft zusätzlich die ersten Bytes der Datei. */
const PREVIEW_TYPES = new Set(["image/png", "image/jpeg", "image/gif", "image/webp"]);

export function isPreviewable(contentType: string): boolean {
  return PREVIEW_TYPES.has(contentType.toLowerCase());
}

const pad = (n: number) => String(n).padStart(2, "0");

/**
 * Name für eine Datei aus der Zwischenablage. Browser liefern Screenshots meist als „image.png“ –
 * daraus wird „Bildschirmfoto 2026-11-21 14-05-09.png“, damit man sie später auseinanderhält.
 */
export function uploadName(file: { name: string; type: string }, now: Date = new Date()): string {
  const name = file.name.trim();
  const generic = name === "" || /^image\.(png|jpe?g|gif|webp)$/i.test(name);
  if (!generic) return name;
  const ext = file.type === "image/jpeg" ? "jpg" : (file.type.split("/")[1] ?? "png").replace(/[^a-z0-9]/gi, "") || "png";
  const stamp = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}-${pad(now.getMinutes())}-${pad(now.getSeconds())}`;
  return `Bildschirmfoto ${stamp}.${ext}`;
}

/** Dateien aus einem Einfüge- oder Ablege-Ereignis; leer, wenn nur Text dabei ist. */
export function filesFrom(data: DataTransfer | null): File[] {
  if (!data) return [];
  if (data.files && data.files.length > 0) return Array.from(data.files);
  // Manche Browser liefern eingefügte Bilder nur über items
  return Array.from(data.items ?? [])
    .filter((item) => item.kind === "file")
    .map((item) => item.getAsFile())
    .filter((f): f is File => f !== null);
}

/** Prüft vor dem Hochladen, damit niemand zehn Sekunden auf eine Absage wartet. */
export function uploadProblem(file: { size: number }): string | null {
  if (file.size === 0) return "Die Datei ist leer.";
  if (file.size > MAX_UPLOAD_BYTES) return `Eine Datei darf höchstens ${MAX_UPLOAD_BYTES / 1024 / 1024} MB groß sein.`;
  return null;
}
