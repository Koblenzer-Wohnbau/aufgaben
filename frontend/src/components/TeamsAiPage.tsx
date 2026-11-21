import { useEffect, useState } from "react";
import { Button, Field, Input, makeStyles, MessageBar, MessageBarBody, Spinner, Text, Textarea, tokens } from "@fluentui/react-components";
import { CheckmarkCircleFilled } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { plainTitle, teamsDraftText } from "../ai";
import { api } from "../api";
import { useApp } from "../context";
import { closeTeamsDialog } from "../teams";
import type { TeamsDraft } from "../types";
import { AiCaptureDialog } from "./AiCaptureDialog";

const useStyles = makeStyles({
  center: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: "14px",
    height: "100%",
    padding: "24px",
    boxSizing: "border-box",
    textAlign: "center",
  },
  done: { fontSize: "48px", color: tokens.colorPaletteGreenForeground1 },
  page: { display: "flex", flexDirection: "column", gap: "12px", height: "100%", boxSizing: "border-box", padding: "16px 20px" },
  grow: { flex: 1, minHeight: 0, display: "flex", flexDirection: "column", gap: "12px", overflowY: "auto" },
  actions: { display: "flex", justifyContent: "flex-end", gap: "8px" },
});

/**
 * Fenster, das Teams nach „Als Aufgabe“ an einer Nachricht öffnet: holt den kurz gespeicherten Text und
 * zeigt den KI-Dialog als Seite. Ohne KI auf dem Server gibt es ein einfaches Formular.
 */
export function TeamsAiPage({ draftId }: { draftId: string }) {
  const styles = useStyles();
  const { me } = useApp();
  const draft = useQuery({ queryKey: ["teams-entwurf", draftId], queryFn: () => api.teamsDraft(draftId), retry: false, staleTime: Infinity });
  const [done, setDone] = useState<string | null>(null);

  // Nach dem Anlegen kurz bestätigen, dann schließt sich das Fenster von selbst.
  useEffect(() => {
    if (!done) return undefined;
    const timer = setTimeout(closeTeamsDialog, 1800);
    return () => clearTimeout(timer);
  }, [done]);

  if (done) {
    return (
      <div className={styles.center} role="status">
        <CheckmarkCircleFilled className={styles.done} />
        <Text size={500} weight="semibold">
          {done}
        </Text>
        <Button onClick={closeTeamsDialog}>Schließen</Button>
      </div>
    );
  }
  if (draft.isLoading) {
    return (
      <div className={styles.center}>
        <Spinner label="Nachricht wird übernommen …" />
      </div>
    );
  }
  if (draft.error || !draft.data) {
    return (
      <div className={styles.center}>
        <MessageBar intent="error">
          <MessageBarBody>{draft.error instanceof Error ? draft.error.message : "Die Nachricht ließ sich nicht übernehmen."}</MessageBarBody>
        </MessageBar>
        <Button onClick={closeTeamsDialog}>Schließen</Button>
      </div>
    );
  }
  if (!me.aiAvailable) {
    return <PlainCapture draft={draft.data} onCreated={setDone} />;
  }
  return (
    <AiCaptureDialog
      inline
      initial={{ text: teamsDraftText(draft.data), link: draft.data.link ?? undefined }}
      onClose={closeTeamsDialog}
      onCreated={setDone}
    />
  );
}

/** Ohne KI: Titel aus der ersten Zeile, die ganze Nachricht und der Link kommen in die Notizen. */
function PlainCapture({ draft, onCreated }: { draft: TeamsDraft; onCreated: (message: string) => void }) {
  const styles = useStyles();
  const { notify } = useApp();
  const queryClient = useQueryClient();
  const [title, setTitle] = useState(() => plainTitle(draft.text));
  const [notes, setNotes] = useState([draft.from ? `Von ${draft.from}:` : null, draft.text, draft.link ? `Teams-Nachricht: ${draft.link}` : null].filter(Boolean).join("\n\n"));
  const [saving, setSaving] = useState(false);

  const create = async () => {
    if (!title.trim()) return;
    setSaving(true);
    try {
      await api.createTask({ title: title.trim(), notes: notes.trim() || undefined });
      await queryClient.invalidateQueries({ queryKey: ["tasks"] });
      onCreated("Aufgabe angelegt");
    } catch (e) {
      notify(e instanceof Error ? e.message : "Die Aufgabe ließ sich nicht anlegen.", "error");
      setSaving(false);
    }
  };

  return (
    <main className={styles.page}>
      <Text as="h1" size={500} weight="semibold">
        Als Aufgabe
      </Text>
      <div className={styles.grow}>
        <Field label="Titel" required>
          <Input value={title} autoFocus maxLength={200} onChange={(_, d) => setTitle(d.value)} onKeyDown={(e) => e.key === "Enter" && void create()} />
        </Field>
        <Field label="Notizen">
          <Textarea value={notes} rows={10} resize="vertical" onChange={(_, d) => setNotes(d.value)} />
        </Field>
      </div>
      <div className={styles.actions}>
        <Button onClick={closeTeamsDialog}>Abbrechen</Button>
        <Button appearance="primary" disabled={saving || !title.trim()} onClick={() => void create()}>
          {saving ? "Lege an …" : "Aufgabe anlegen"}
        </Button>
      </div>
    </main>
  );
}
