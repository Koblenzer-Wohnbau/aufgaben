import { authHeaders } from "./api";

export interface LiveMessage {
  type: "task" | "task-deleted" | "lists" | "absences" | "features" | "verbunden";
  id?: number;
}

/**
 * Live-Updates über Server-Sent Events. Bewusst mit fetch statt EventSource,
 * weil in Teams ein Bearer-Token mitgeschickt werden muss.
 */
export function connectLive(onMessage: (m: LiveMessage) => void, onState: (online: boolean) => void): () => void {
  let stopped = false;
  let controller: AbortController | null = null;

  const run = async () => {
    while (!stopped) {
      controller = new AbortController();
      try {
        const res = await fetch("/api/events", {
          headers: await authHeaders(),
          credentials: "same-origin",
          signal: controller.signal,
        });
        if (!res.ok || !res.body) throw new Error(String(res.status));
        onState(true);
        onMessage({ type: "verbunden" });
        const reader = res.body.pipeThrough(new TextDecoderStream()).getReader();
        let buffer = "";
        for (;;) {
          const { value, done } = await reader.read();
          if (done) break;
          buffer += value;
          let idx: number;
          while ((idx = buffer.indexOf("\n\n")) >= 0) {
            const chunk = buffer.slice(0, idx);
            buffer = buffer.slice(idx + 2);
            for (const line of chunk.split("\n")) {
              if (!line.startsWith("data: ")) continue;
              try {
                onMessage(JSON.parse(line.slice(6)) as LiveMessage);
              } catch {
                /* ungültige Nachricht ignorieren */
              }
            }
          }
        }
      } catch {
        /* Verbindungsabbruch, gleich neu verbinden */
      }
      onState(false);
      if (!stopped) await new Promise((r) => setTimeout(r, 4000));
    }
  };
  void run();

  return () => {
    stopped = true;
    controller?.abort();
  };
}
