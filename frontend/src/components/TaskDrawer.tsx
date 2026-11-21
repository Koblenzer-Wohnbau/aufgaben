import { useEffect, useRef, useState } from "react";
import {
  Avatar,
  Button,
  Caption1,
  Checkbox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Divider,
  Dropdown,
  DrawerBody,
  DrawerHeader,
  DrawerHeaderTitle,
  Field,
  InlineDrawer,
  Input,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Option,
  OverlayDrawer,
  Spinner,
  Switch,
  Text,
  Textarea,
  tokens,
} from "@fluentui/react-components";
import {
  AddRegular,
  ArrowDownloadRegular,
  AttachRegular,
  CheckmarkCircleFilled,
  CircleRegular,
  DeleteRegular,
  DismissRegular,
  SendRegular,
} from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { filesFrom, isPreviewable, uploadName, uploadProblem } from "../attachments";
import { initials, useApp } from "../context";
import { fromLocalInput, RECURRENCE_LABEL, shortDateTime, toLocalInput } from "../dates";
import { eventLine } from "../eventText";
import { TRASH_DAYS } from "../selection";
import type { Attachment, Task, TaskEvent } from "../types";
import { LaterPanel } from "./LaterPanel";
import { PersonPicker } from "./PersonPicker";

const useStyles = makeStyles({
  drawer: { width: "420px", borderLeft: `1px solid ${tokens.colorNeutralStroke2}` },
  headerActions: { display: "flex", gap: "2px" },
  body: {
    display: "flex",
    flexDirection: "column",
    gap: "14px",
    paddingBottom: "32px",
    overflowX: "hidden",
    // Fluent-Felder haben eine Mindestbreite – hier sollen sie sich der Spalte anpassen.
    "& .fui-Dropdown, & .fui-Input, & .fui-Combobox, & .fui-Textarea": { minWidth: 0, width: "100%", boxSizing: "border-box" },
  },
  titleRow: { display: "flex", alignItems: "flex-start", gap: "8px" },
  check: { border: "none", background: "transparent", cursor: "pointer", fontSize: "26px", padding: "4px 0", color: tokens.colorNeutralForeground3, display: "flex" },
  checkDone: { color: tokens.colorPaletteGreenForeground1, animationName: "aufgaben-pop", animationDuration: "260ms" },
  titleInput: { flex: 1, fontSize: tokens.fontSizeBase500, fontWeight: tokens.fontWeightSemibold },
  grid: { display: "grid", gridTemplateColumns: "minmax(0, 1fr) minmax(0, 1fr)", gap: "10px" },
  checkItem: { display: "flex", alignItems: "center", gap: "4px" },
  meta: { color: tokens.colorNeutralForeground3 },
  event: { display: "flex", gap: "8px", alignItems: "flex-start", padding: "6px 0" },
  comment: { backgroundColor: tokens.colorNeutralBackground3, borderRadius: tokens.borderRadiusMedium, padding: "6px 10px", whiteSpace: "pre-wrap" },
  commentBox: { display: "flex", gap: "8px", alignItems: "flex-end" },
  footer: { display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "8px" },
  attachment: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    padding: "6px 8px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    marginBottom: "4px",
  },
  attachmentName: { flex: 1, minWidth: 0, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
  thumbButton: { border: "none", padding: 0, background: "transparent", cursor: "zoom-in", display: "flex", flexShrink: 0 },
  thumb: { width: "44px", height: "44px", objectFit: "cover", borderRadius: tokens.borderRadiusSmall, border: `1px solid ${tokens.colorNeutralStroke2}` },
  attachActions: { display: "flex", alignItems: "center", gap: "8px", marginTop: "4px" },
  editor: { display: "flex", flexDirection: "column", gap: "14px", borderRadius: tokens.borderRadiusMedium, outline: "2px dashed transparent", outlineOffset: "6px" },
  dropActive: { outline: `2px dashed ${tokens.colorBrandStroke1}`, backgroundColor: tokens.colorBrandBackground2 },
  previewImage: { maxWidth: "100%", maxHeight: "70vh", display: "block", margin: "0 auto", borderRadius: tokens.borderRadiusMedium },
  // Gruppiert Felder, die gemeinsam gesperrt werden (z. B. bei einer Lese-Freigabe).
  fieldset: { border: "none", padding: 0, margin: 0, minWidth: 0, display: "flex", flexDirection: "column", gap: "14px" },
});

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} MB`;
}

/** Kleine Vorschau für Bildanhänge – mit Anmeldung geladen (in Teams per Token), danach freigegeben. */
function AttachmentThumb({ taskId, attachment, onOpen }: { taskId: number; attachment: Attachment; onOpen: (url: string) => void }) {
  const styles = useStyles();
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let cancelled = false;
    let created: string | null = null;
    api
      .attachmentPreview(taskId, attachment.id)
      .then((u) => {
        if (cancelled) URL.revokeObjectURL(u);
        else {
          created = u;
          setUrl(u);
        }
      })
      .catch(() => setUrl(null));
    return () => {
      cancelled = true;
      if (created) URL.revokeObjectURL(created);
    };
  }, [taskId, attachment.id]);
  if (!url) return <AttachRegular />;
  return (
    <button type="button" className={styles.thumbButton} onClick={() => onOpen(url)} aria-label={`${attachment.fileName} groß anzeigen`}>
      <img src={url} alt="" className={styles.thumb} />
    </button>
  );
}

export function TaskDrawer({ taskId, onClose, overlay }: { taskId: number | null; onClose: () => void; overlay: boolean }) {
  const styles = useStyles();
  const { notify } = useApp();
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ["task", taskId],
    queryFn: () => api.task(taskId!),
    enabled: taskId !== null,
  });

  // Oben neben „Schließen“, damit niemand bis ans Ende des Verlaufs scrollen muss
  const moveToTrash = async () => {
    const task = query.data?.task;
    if (!task) return;
    try {
      await api.deleteTask(task.id);
      notify(`In den Papierkorb gelegt – dort ${TRASH_DAYS} Tage lang wiederherstellbar`);
      onClose();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["tasks"] }),
        queryClient.invalidateQueries({ queryKey: ["counts"] }),
      ]);
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  const content = (
    <>
      <DrawerHeader>
        <DrawerHeaderTitle
          action={
            <div className={styles.headerActions}>
              {query.data?.task.canDelete && (
                <Button appearance="subtle" icon={<DeleteRegular />} aria-label="In den Papierkorb" title="In den Papierkorb" onClick={() => void moveToTrash()} />
              )}
              <Button appearance="subtle" icon={<DismissRegular />} aria-label="Schließen" onClick={onClose} />
            </div>
          }
        >
          Aufgabe
        </DrawerHeaderTitle>
      </DrawerHeader>
      <DrawerBody className={styles.body}>
        {query.isLoading && <Spinner />}
        {query.error && <Text>{query.error instanceof Error ? query.error.message : "Fehler"}</Text>}
        {query.data && (
          <TaskEditor
            key={query.data.task.id}
            task={query.data.task}
            events={query.data.events}
            canEdit={query.data.canEdit}
            viaGrant={query.data.viaGrant}
            onDeleted={onClose}
          />
        )}
      </DrawerBody>
    </>
  );

  if (overlay) {
    return (
      <OverlayDrawer open={taskId !== null} position="end" size="medium" onOpenChange={(_, d) => !d.open && onClose()}>
        {content}
      </OverlayDrawer>
    );
  }
  return (
    <InlineDrawer open={taskId !== null} position="end" className={styles.drawer}>
      {content}
    </InlineDrawer>
  );
}

interface EditorProps {
  task: Task;
  events: TaskEvent[];
  /** false: nur über eine Lese-Freigabe sichtbar. */
  canEdit: boolean;
  /** true: nicht eigene Aufgabe, sichtbar über ein Einsichtsrecht. */
  viaGrant: boolean;
  onDeleted: () => void;
}

function TaskEditor({ task, events, canEdit, viaGrant, onDeleted }: EditorProps) {
  const styles = useStyles();
  const { projects, lists, users, me, notify, confirm } = useApp();
  const queryClient = useQueryClient();
  const [title, setTitle] = useState(task.title);
  const [notes, setNotes] = useState(task.notes);
  const [newItem, setNewItem] = useState("");
  const [comment, setComment] = useState("");
  const [uploading, setUploading] = useState(0);
  const [dragOver, setDragOver] = useState(false);
  const [preview, setPreview] = useState<{ url: string; attachment: Attachment } | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const done = task.status === "erledigt";

  // Live-Änderungen übernehmen – aber nie Text überschreiben, den die Person gerade tippt.
  const serverTitle = useRef(task.title);
  const serverNotes = useRef(task.notes);
  useEffect(() => {
    setTitle((current) => (current === serverTitle.current ? task.title : current));
    serverTitle.current = task.title;
  }, [task.title]);
  useEffect(() => {
    setNotes((current) => (current === serverNotes.current ? task.notes : current));
    serverNotes.current = task.notes;
  }, [task.notes]);

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["task", task.id] }),
      queryClient.invalidateQueries({ queryKey: ["tasks"] }),
      queryClient.invalidateQueries({ queryKey: ["counts"] }),
    ]);

  const run = async (fn: () => Promise<unknown>, success?: string): Promise<boolean> => {
    try {
      await fn();
      await refresh();
      if (success) notify(success);
      return true;
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
      await refresh();
      return false;
    }
  };

  // Feldweise Änderungen: Es wird nur das geänderte Feld geschickt, dadurch überschreiben sich
  // zwei Personen nicht gegenseitig andere Felder.
  const patch = (body: Record<string, unknown>, success?: string) => run(() => api.patchTask(task.id, body), success);

  const assignee = users.find((u) => u.id === task.assignee.id) ?? task.assignee;

  // Anhänge: Strg+V (z. B. ein Screenshot), Ziehen und Ablegen oder „Datei anhängen“.
  const uploadFiles = async (files: File[]) => {
    if (!canEdit || files.length === 0) return;
    let added = 0;
    setUploading((n) => n + files.length);
    for (const file of files) {
      const problem = uploadProblem(file);
      try {
        if (problem) throw new Error(`${file.name || "Datei"}: ${problem}`);
        await api.uploadAttachment(task.id, file, uploadName(file));
        added++;
      } catch (e) {
        notify(e instanceof Error ? e.message : "Hochladen fehlgeschlagen", "error");
      } finally {
        setUploading((n) => n - 1);
      }
    }
    await refresh();
    if (added > 0) notify(added === 1 ? "Anhang hinzugefügt" : `${added} Anhänge hinzugefügt`);
  };

  const onPaste = (e: React.ClipboardEvent) => {
    if (!canEdit) return;
    const files = filesFrom(e.clipboardData);
    if (files.length === 0) return;
    // In einem Textfeld mit Text in der Zwischenablage (z. B. aus Word kopiert): normal einfügen
    const target = e.target as HTMLElement;
    const inTextField = target.closest("input, textarea") !== null;
    if (inTextField && e.clipboardData.types.includes("text/plain")) return;
    e.preventDefault();
    void uploadFiles(files);
  };
  const dragHasFiles = (e: React.DragEvent) => canEdit && Array.from(e.dataTransfer.types).includes("Files");

  return (
    <div
      className={`${styles.editor} ${dragOver ? styles.dropActive : ""}`}
      onPaste={onPaste}
      onDragOver={(e) => {
        if (!dragHasFiles(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = "copy";
        if (!dragOver) setDragOver(true);
      }}
      onDragLeave={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDragOver(false);
      }}
      onDrop={(e) => {
        if (!dragHasFiles(e)) return;
        e.preventDefault();
        setDragOver(false);
        void uploadFiles(filesFrom(e.dataTransfer));
      }}
    >
      {viaGrant && (
        <MessageBar intent={canEdit ? "info" : "warning"}>
          <MessageBarBody>
            {canEdit
              ? "Über ein Einsichtsrecht: Inhalte dürfen geändert werden – Verantwortliche, Projekt und Liste nicht."
              : "Über ein Einsichtsrecht: nur lesen."}
          </MessageBarBody>
        </MessageBar>
      )}
      <fieldset className={styles.fieldset} disabled={!canEdit}>
      <div className={styles.titleRow}>
        <button
          className={`${styles.check} ${done ? styles.checkDone : ""}`}
          aria-label={done ? "Als offen markieren" : "Als erledigt markieren"}
          onClick={() => run(() => (done ? api.reopenTask(task.id) : api.completeTask(task.id)))}
        >
          {done ? <CheckmarkCircleFilled /> : <CircleRegular />}
        </button>
        <Input
          className={styles.titleInput}
          appearance="underline"
          value={title}
          onChange={(_, d) => setTitle(d.value)}
          onBlur={() => title.trim() && title !== task.title && patch({ title })}
          onKeyDown={(e) => e.key === "Enter" && (e.currentTarget as HTMLInputElement).blur()}
        />
      </div>

      {task.assignee.id === me.id && (
        <Switch label="Zu „Mein Tag“ hinzufügen" checked={task.myDay} onChange={(_, d) => patch({ myDay: d.checked })} />
      )}

      {/* Später / Warte auf …: plant die verantwortliche Person (oder ihre Vertretung mit Freigabe) */}
      {!done && <LaterPanel task={task} patch={patch} readOnly={!canEdit || (task.assignee.id !== me.id && !viaGrant)} />}

      <fieldset className={styles.fieldset} disabled={viaGrant}>
      <Field label="Verantwortlich" hint={task.isDelegated ? `Angelegt von ${task.createdBy.name}` : undefined}>
        <PersonPicker
          users={users}
          value={assignee}
          onChange={(u) => u && u.id !== task.assignee.id && patch({ assigneeId: u.id }, u.id === me.id ? "Wieder bei dir" : `An ${u.name} übergeben`)}
        />
      </Field>
      </fieldset>

      <div className={styles.grid}>
        <Field label="Fällig am">
          <Input type="date" value={task.dueDate ?? ""} onChange={(_, d) => patch({ dueDate: d.value || null })} />
        </Field>
        <Field label="Uhrzeit">
          <Input type="time" value={task.dueTime ?? ""} disabled={!task.dueDate} onChange={(_, d) => patch({ dueTime: d.value || null })} />
        </Field>
        <Field label="Erinnerung">
          <Input type="datetime-local" value={toLocalInput(task.remindAt)} onChange={(_, d) => patch({ remindAt: fromLocalInput(d.value) })} />
        </Field>
        <Field label="Wiederholung">
          <Dropdown
            value={RECURRENCE_LABEL[task.recurrence]}
            selectedOptions={[task.recurrence]}
            onOptionSelect={(_, d) => patch({ recurrence: d.optionValue ?? "" })}
          >
            {Object.entries(RECURRENCE_LABEL).map(([value, label]) => (
              <Option key={value || "keine"} value={value}>
                {label}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <Field label="Priorität">
          <Dropdown
            value={{ hoch: "Hoch", normal: "Normal", niedrig: "Niedrig" }[task.priority]}
            selectedOptions={[task.priority]}
            onOptionSelect={(_, d) => d.optionValue && patch({ priority: d.optionValue })}
          >
            <Option value="hoch">Hoch</Option>
            <Option value="normal">Normal</Option>
            <Option value="niedrig">Niedrig</Option>
          </Dropdown>
        </Field>
        <Field label="Art">
          <Dropdown
            value={task.kind === "wiedervorlage" ? "Wiedervorlage" : "Aufgabe"}
            selectedOptions={[task.kind]}
            onOptionSelect={(_, d) => d.optionValue && patch({ kind: d.optionValue })}
          >
            <Option value="aufgabe">Aufgabe</Option>
            <Option value="wiedervorlage">Wiedervorlage</Option>
          </Dropdown>
        </Field>
      </div>

      <Field label="Projekt" hint={task.project ? "Projektaufgaben sehen alle Kolleginnen und Kollegen" : undefined}>
        <Dropdown
          disabled={viaGrant}
          value={task.project?.label ?? "Kein Projekt"}
          selectedOptions={[String(task.project?.id ?? "")]}
          onOptionSelect={(_, d) => patch({ projectId: d.optionValue ? Number(d.optionValue) : null })}
        >
          <Option value="">Kein Projekt</Option>
          {projects.map((p) => (
            <Option key={p.id} value={String(p.id)} text={p.label}>
              {p.label}
            </Option>
          ))}
        </Dropdown>
      </Field>

      {(task.createdBy.id === me.id || task.assignee.id === me.id) && (
        <Field label="Liste">
          <Dropdown
            value={lists.find((l) => l.id === task.listId)?.name ?? "Keine Liste"}
            selectedOptions={[String(task.listId ?? "")]}
            onOptionSelect={(_, d) => patch({ listId: d.optionValue ? Number(d.optionValue) : null })}
          >
            <Option value="">Keine Liste</Option>
            {lists.map((l) => (
              <Option key={l.id} value={String(l.id)} text={l.name}>
                {l.name}
              </Option>
            ))}
          </Dropdown>
        </Field>
      )}

      <Field label={`Checkliste ${task.checklist.length ? `(${task.checklist.filter((c) => c.done).length}/${task.checklist.length})` : ""}`}>
        <div>
          {task.checklist.map((item) => (
            <div key={item.id} className={styles.checkItem}>
              <Checkbox
                checked={item.done}
                label={<span style={{ textDecoration: item.done ? "line-through" : undefined }}>{item.title}</span>}
                onChange={(_, d) => run(() => api.updateChecklist(task.id, item.id, { done: !!d.checked }))}
                style={{ flex: 1 }}
              />
              <Button size="small" appearance="subtle" icon={<DeleteRegular />} aria-label="Punkt löschen" onClick={() => run(() => api.deleteChecklist(task.id, item.id))} />
            </div>
          ))}
          <Input
            appearance="underline"
            contentBefore={<AddRegular />}
            placeholder="Schritt hinzufügen"
            value={newItem}
            onChange={(_, d) => setNewItem(d.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && newItem.trim()) {
                const value = newItem;
                setNewItem("");
                void run(() => api.addChecklist(task.id, value));
              }
            }}
            style={{ width: "100%" }}
          />
        </div>
      </Field>
      </fieldset>

      {(canEdit || task.attachments.length > 0) && (
        <Field
          label={`Anhänge${task.attachments.length ? ` (${task.attachments.length})` : ""}`}
          hint={
            canEdit
              ? "Bild mit Strg+V einfügen oder Dateien hierher ziehen (bis 10 MB). Anhänge werden heruntergeladen, nie im Browser geöffnet."
              : "Anhänge werden heruntergeladen, nie im Browser geöffnet."
          }
        >
          <div>
            {task.attachments.map((a) => (
              <div key={a.id} className={styles.attachment}>
                {isPreviewable(a.contentType) ? (
                  <AttachmentThumb taskId={task.id} attachment={a} onOpen={(url) => setPreview({ url, attachment: a })} />
                ) : (
                  <AttachRegular />
                )}
                <Text className={styles.attachmentName} title={a.fileName}>
                  {a.fileName}
                </Text>
                <Caption1 className={styles.meta}>{formatSize(a.size)}</Caption1>
                <Button
                  size="small"
                  appearance="subtle"
                  icon={<ArrowDownloadRegular />}
                  aria-label={`${a.fileName} herunterladen`}
                  onClick={() => api.downloadAttachment(task.id, a.id, a.fileName).catch((e) => notify(e.message, "error"))}
                />
                {canEdit && (
                  <Button
                    size="small"
                    appearance="subtle"
                    icon={<DeleteRegular />}
                    aria-label={`${a.fileName} entfernen`}
                    onClick={() =>
                      void confirm({ title: "Anhang entfernen?", text: a.fileName, confirmLabel: "Entfernen" }).then(
                        (ok) => ok && run(() => api.deleteAttachment(task.id, a.id), "Anhang entfernt"),
                      )
                    }
                  />
                )}
              </div>
            ))}
            {canEdit && (
              <div className={styles.attachActions}>
                <Button size="small" icon={<AttachRegular />} disabled={uploading > 0} onClick={() => fileInput.current?.click()}>
                  Datei anhängen
                </Button>
                {uploading > 0 && <Spinner size="extra-tiny" label="Wird hochgeladen …" />}
                <input
                  ref={fileInput}
                  type="file"
                  multiple
                  hidden
                  onChange={(e) => {
                    const files = Array.from(e.target.files ?? []);
                    e.target.value = "";
                    void uploadFiles(files);
                  }}
                />
              </div>
            )}
          </div>
        </Field>
      )}

      <Dialog open={preview !== null} onOpenChange={(_, d) => !d.open && setPreview(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>{preview?.attachment.fileName}</DialogTitle>
            <DialogContent>{preview && <img src={preview.url} alt={preview.attachment.fileName} className={styles.previewImage} />}</DialogContent>
            <DialogActions>
              <Button
                icon={<ArrowDownloadRegular />}
                onClick={() =>
                  preview &&
                  api.downloadAttachment(task.id, preview.attachment.id, preview.attachment.fileName).catch((e) => notify(e.message, "error"))
                }
              >
                Herunterladen
              </Button>
              <Button appearance="primary" onClick={() => setPreview(null)}>
                Schließen
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Field label="Notizen">
        <Textarea
          resize="vertical"
          rows={4}
          value={notes}
          readOnly={!canEdit}
          onChange={(_, d) => setNotes(d.value)}
          onBlur={() => canEdit && notes !== task.notes && patch({ notes })}
        />
      </Field>

      <Divider />
      <Text weight="semibold">Verlauf und Kommentare</Text>
      <div>
        {events.map((e) => (
          <div key={e.id} className={styles.event}>
            <Avatar size={24} name={e.actor?.name ?? "System"} initials={initials(e.actor?.name ?? "S")} color="colorful" />
            <div style={{ flex: 1, minWidth: 0 }}>
              <Caption1 className={styles.meta}>
                <b>{e.actor?.name ?? "System"}</b>{" "}
                {eventLine(e)} · {shortDateTime(e.createdAt)}
              </Caption1>
              {e.kind === "kommentar" && <div className={styles.comment}>{e.text}</div>}
            </div>
          </div>
        ))}
      </div>
      <fieldset className={`${styles.fieldset} ${styles.commentBox}`} disabled={!canEdit}>
        <Textarea
          style={{ flex: 1 }}
          placeholder={task.isDelegated ? "Kommentar – die andere Person wird benachrichtigt" : "Kommentar"}
          value={comment}
          onChange={(_, d) => setComment(d.value)}
          resize="vertical"
        />
        <Button
          icon={<SendRegular />}
          appearance="primary"
          disabled={!comment.trim()}
          aria-label="Kommentar senden"
          onClick={() => {
            const value = comment;
            setComment("");
            void run(() => api.comment(task.id, value));
          }}
        />
      </fieldset>

      <div className={styles.footer}>
        <Caption1 className={styles.meta}>
          Angelegt {shortDateTime(task.createdAt)} von {task.createdBy.name}
        </Caption1>
        {task.canDelete && (
          <Button
            appearance="subtle"
            icon={<DeleteRegular />}
            onClick={() =>
              void run(() => api.deleteTask(task.id), `In den Papierkorb gelegt – dort ${TRASH_DAYS} Tage lang wiederherstellbar`).then(
                (ok) => ok && onDeleted(),
              )
            }
          >
            In den Papierkorb
          </Button>
        )}
      </div>
    </div>
  );
}
