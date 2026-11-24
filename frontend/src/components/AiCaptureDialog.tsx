import { useEffect, useRef, useState, type ClipboardEvent } from "react";
import {
  Badge,
  Button,
  Caption1,
  Checkbox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Dropdown,
  Field,
  Input,
  makeStyles,
  mergeClasses,
  MessageBar,
  MessageBarBody,
  Option,
  Spinner,
  Text,
  Textarea,
  tokens,
} from "@fluentui/react-components";
import { DismissRegular, QuestionCircleRegular, SparkleFilled, SparkleRegular, StarFilled, StarRegular } from "@fluentui/react-icons";
import { useQueryClient } from "@tanstack/react-query";
import { applyAnswer, clipboardInput, createdMessage, currentOption, draftNotes, imageToDataUrl, modelLabel, thinkingText } from "../ai";
import { api } from "../api";
import { useApp } from "../context";
import type { AiInput, AiQuestion, AiTaskDraft } from "../types";
import { PersonPicker } from "./PersonPicker";

const useStyles = makeStyles({
  surface: { maxWidth: "820px", width: "100%" },
  title: { display: "flex", alignItems: "center", gap: "8px" },
  sparkle: { color: tokens.colorBrandForeground1 },
  content: {
    display: "flex",
    flexDirection: "column",
    gap: "12px",
    // Fluent-Felder haben eine Mindestbreite, in den Karten sollen sie sich der Spalte anpassen.
    "& .fui-Dropdown, & .fui-Input, & .fui-Combobox": { minWidth: 0, width: "100%", boxSizing: "border-box" },
  },
  muted: { color: tokens.colorNeutralForeground3 },
  imageRow: { display: "flex", alignItems: "center", gap: "12px", padding: "8px", borderRadius: tokens.borderRadiusMedium, backgroundColor: tokens.colorNeutralBackground3 },
  thumb: { width: "96px", maxHeight: "72px", objectFit: "cover", borderRadius: tokens.borderRadiusSmall, border: `1px solid ${tokens.colorNeutralStroke2}` },
  thinking: { display: "flex", alignItems: "center", gap: "10px", color: tokens.colorBrandForeground1 },
  card: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    padding: "12px 14px",
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow4,
    animationName: "aufgaben-fade-in",
    animationDuration: "220ms",
  },
  cardOff: { opacity: 0.5 },
  cardHead: { display: "flex", alignItems: "center", gap: "6px" },
  titleInput: { flex: 1, minWidth: 0, fontWeight: tokens.fontWeightSemibold },
  fields: {
    display: "grid",
    gridTemplateColumns: "minmax(0, 0.8fr) minmax(0, 1.2fr) minmax(0, 1fr)",
    gap: "8px",
    "@media (max-width: 640px)": { gridTemplateColumns: "minmax(0, 1fr)" },
  },
  unclear: { borderLeft: `3px solid ${tokens.colorPaletteMarigoldBorderActive}`, paddingLeft: "6px" },
  question: {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    gap: "6px 10px",
    padding: "8px 10px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorPaletteMarigoldBackground1,
  },
  questionIcon: { color: tokens.colorPaletteMarigoldForeground1, fontSize: "18px", flexShrink: 0 },
  options: { display: "flex", flexWrap: "wrap", gap: "6px" },
  notes: { color: tokens.colorNeutralForeground2, whiteSpace: "pre-wrap" },
  checklist: { margin: 0, paddingLeft: "20px", color: tokens.colorNeutralForeground2 },
  source: { color: tokens.colorNeutralForeground3, fontStyle: "italic" },
  meta: { display: "flex", flexWrap: "wrap", gap: "6px" },
  page: {
    display: "flex",
    flexDirection: "column",
    gap: "12px",
    height: "100%",
    boxSizing: "border-box",
    padding: "16px 20px",
    backgroundColor: tokens.colorNeutralBackground2,
  },
  pageBody: { flex: 1, minHeight: 0, overflowY: "auto", paddingRight: "4px" },
  pageActions: { display: "flex", justifyContent: "flex-end", gap: "8px" },
});

type Phase = "input" | "thinking" | "review";

interface Draft extends AiTaskDraft {
  include: boolean;
  /** beantwortete Rückfragen (Feldname) */
  answered: string[];
}

/**
 * „Mit KI erfassen“: Text aus einer Teams-Nachricht, Mail oder einem Protokoll, oder ein Bildschirmfoto:
 * wird zu Vorschlägen. Angelegt wird erst, wenn die Person bestätigt; Unklares fragt die KI direkt nach.
 */
export function AiCaptureDialog({
  initial,
  onClose,
  inline = false,
  onCreated,
}: {
  initial: AiInput;
  onClose: () => void;
  /** als ganze Seite statt als Dialog (Fenster in Teams) */
  inline?: boolean;
  /** statt Meldung und Schließen: die Seite zeigt selbst, was angelegt wurde */
  onCreated?: (message: string) => void;
}) {
  const styles = useStyles();
  const { me, users, projects, notify } = useApp();
  const queryClient = useQueryClient();
  const [text, setText] = useState(initial.text ?? "");
  const [image, setImage] = useState<string | null>(null);
  const [preparing, setPreparing] = useState(!!initial.image);
  const [phase, setPhase] = useState<Phase>("input");
  const [seconds, setSeconds] = useState(0);
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [note, setNote] = useState<string | null>(null);
  const [model, setModel] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const abort = useRef<AbortController | null>(null);
  const autoStart = useRef(!!(initial.text || initial.image));
  const meRef = users.find((u) => u.id === me.id) ?? { id: me.id, name: me.name, email: me.email, jobTitle: null };

  const loadImage = async (blob: Blob) => {
    setPreparing(true);
    setError(null);
    try {
      setImage(await imageToDataUrl(blob));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Das Bild ließ sich nicht lesen.");
    } finally {
      setPreparing(false);
    }
  };

  useEffect(() => {
    if (initial.image) void loadImage(initial.image);
    // nur beim Öffnen
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (phase !== "thinking") return;
    const timer = setInterval(() => setSeconds((s) => s + 1), 1000);
    return () => clearInterval(timer);
  }, [phase]);

  const suggest = async () => {
    if ((!text.trim() && !image) || preparing) return;
    setPhase("thinking");
    setSeconds(0);
    setError(null);
    const controller = new AbortController();
    abort.current = controller;
    try {
      const result = await api.aiSuggest(text, image, controller.signal);
      setDrafts(result.tasks.map((t) => ({ ...t, include: true, answered: [] })));
      setNote(result.note);
      setModel(result.model);
      setPhase("review");
    } catch (e) {
      if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "Der KI-Vorschlag ist fehlgeschlagen.");
      setPhase("input");
    }
  };

  // Mit Strg+V geöffnet: gleich loslegen, sobald ein Bild fertig verkleinert ist.
  useEffect(() => {
    if (autoStart.current && !preparing && phase === "input" && (text.trim() || image)) {
      autoStart.current = false;
      void suggest();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preparing, image]);

  const cancel = () => abort.current?.abort();

  const update = (index: number, patch: Partial<Draft>) => setDrafts((list) => list.map((d, i) => (i === index ? { ...d, ...patch } : d)));

  const answer = (index: number, question: AiQuestion, value: string) => {
    const option = question.options.find((o) => o.value === value);
    if (!option) return;
    setDrafts((list) =>
      list.map((d, i) => (i === index ? { ...applyAnswer(d, question, option, users), answered: [...d.answered, question.field] } : d)),
    );
  };

  const onPaste = (e: ClipboardEvent) => {
    const input = clipboardInput(e.clipboardData);
    if (input?.image) {
      e.preventDefault();
      void loadImage(input.image);
    }
  };

  const chosen = drafts.filter((d) => d.include && d.title.trim());

  const create = async () => {
    setSaving(true);
    let created = 0;
    let createdForMe = false;
    const notified = new Set<string>();
    for (const d of chosen) {
      try {
        const task = await api.createTask({
          title: d.title.trim(),
          notes: draftNotes(d, initial.link),
          priority: d.priority,
          kind: d.kind,
          assigneeId: d.assignee && d.assignee.id !== me.id ? d.assignee.id : null,
          projectId: d.project?.id ?? null,
          dueDate: d.dueDate,
          dueTime: d.dueDate ? d.dueTime : null,
          checklist: d.checklist,
          fromAi: true,
        });
        created++;
        if (task.assignee.id === me.id) createdForMe = true;
        if (d.assignee && d.assignee.id !== me.id) notified.add(d.assignee.name);
      } catch (e) {
        notify(`„${d.title}“: ${e instanceof Error ? e.message : "nicht angelegt"}`, "error");
      }
    }
    setSaving(false);
    await Promise.all([queryClient.invalidateQueries({ queryKey: ["tasks"] }), queryClient.invalidateQueries({ queryKey: ["counts"] })]);
    if (created > 0) {
      const destination = !me.showInbox && createdForMe ? ". Eigene Aufgaben findest du unter „Geplant“." : "";
      const message = createdMessage(created, [...notified]) + destination;
      if (onCreated) {
        onCreated(message);
      } else {
        notify(message);
        onClose();
      }
    }
  };

  const close = () => {
    if (phase === "thinking") cancel();
    if (!saving) onClose();
  };

  const heading = (
    <span className={styles.title}>
      <SparkleFilled className={styles.sparkle} /> Mit KI erfassen
    </span>
  );

  const body = (
    <>
            {phase !== "review" && (
              <>
                <Text className={styles.muted}>
                  Text aus einer Teams-Nachricht, Mail oder einem Protokoll einfügen, oder ein Bildschirmfoto mit Strg+V. Die KI schlägt Aufgaben vor,
                  angelegt wird erst nach deiner Bestätigung.
                </Text>
                <Textarea
                  autoFocus
                  rows={8}
                  resize="vertical"
                  value={text}
                  disabled={phase === "thinking"}
                  placeholder="z. B. „Kannst du bis Freitag das Angebot vom Dachdecker für den Lindenpark prüfen? Thomas bereitet danach den Auftrag vor.“"
                  onChange={(_, d) => setText(d.value)}
                  onPaste={onPaste}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) void suggest();
                  }}
                />
                {(image || preparing) && (
                  <div className={styles.imageRow}>
                    {image ? <img src={image} alt="Eingefügtes Bildschirmfoto" className={styles.thumb} /> : <Spinner size="tiny" />}
                    <Text style={{ flex: 1 }}>{preparing ? "Bild wird vorbereitet …" : "Bildschirmfoto: Der Text darauf wird erkannt"}</Text>
                    {image && phase !== "thinking" && <Button appearance="subtle" icon={<DismissRegular />} aria-label="Bild entfernen" onClick={() => setImage(null)} />}
                  </div>
                )}
                {phase === "thinking" && (
                  <div className={styles.thinking} role="status">
                    <Spinner size="small" />
                    <Text>{thinkingText(seconds)}</Text>
                  </div>
                )}
              </>
            )}
            {error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {phase === "review" && (
              <>
                {note && (
                  <MessageBar intent={drafts.length === 0 ? "warning" : "info"}>
                    <MessageBarBody>{note}</MessageBarBody>
                  </MessageBar>
                )}
                {drafts.map((d, i) => {
                  const open = d.questions.filter((q) => !d.answered.includes(q.field));
                  const unclear = (field: AiQuestion["field"]) => open.some((q) => q.field === field);
                  return (
                    <div key={i} className={mergeClasses(styles.card, !d.include && styles.cardOff)}>
                      <div className={styles.cardHead}>
                        <Checkbox shape="circular" checked={d.include} onChange={(_, v) => update(i, { include: !!v.checked })} aria-label="Diese Aufgabe anlegen" />
                        <Input className={styles.titleInput} appearance="underline" value={d.title} aria-label="Titel" onChange={(_, v) => update(i, { title: v.value })} />
                        <Button
                          appearance="subtle"
                          icon={d.priority === "hoch" ? <StarFilled /> : <StarRegular />}
                          aria-label="Wichtig umschalten"
                          onClick={() => update(i, { priority: d.priority === "hoch" ? "normal" : "hoch" })}
                        />
                      </div>
                      <div className={styles.fields}>
                        <Field label="Fällig am" className={unclear("faellig") ? styles.unclear : undefined}>
                          <Input type="date" size="small" value={d.dueDate ?? ""} onChange={(_, v) => update(i, { dueDate: v.value || null })} />
                        </Field>
                        <Field label="Verantwortlich" className={unclear("verantwortlich") ? styles.unclear : undefined}>
                          <PersonPicker users={users} value={d.assignee ?? meRef} onChange={(u) => u && update(i, { assignee: u.id === me.id ? null : u })} />
                        </Field>
                        <Field label="Projekt" className={unclear("projekt") ? styles.unclear : undefined}>
                          <Dropdown
                            size="small"
                            value={d.project?.label ?? "Kein Projekt"}
                            selectedOptions={[String(d.project?.id ?? "")]}
                            onOptionSelect={(_, o) => update(i, { project: o.optionValue ? { id: Number(o.optionValue), label: o.optionText ?? "" } : null })}
                          >
                            <Option value="">Kein Projekt</Option>
                            {projects.map((p) => (
                              <Option key={p.id} value={String(p.id)} text={p.label}>
                                {p.label}
                              </Option>
                            ))}
                          </Dropdown>
                        </Field>
                      </div>
                      {open.map((q) => (
                        <div key={q.field} className={styles.question}>
                          <QuestionCircleRegular className={styles.questionIcon} />
                          <Text weight="semibold">{q.text}</Text>
                          <div className={styles.options}>
                            {q.options.map((o) => (
                              <Button key={o.value} size="small" appearance={currentOption(d, q, me.id) === o.value ? "primary" : "outline"} onClick={() => answer(i, q, o.value)}>
                                {o.label}
                              </Button>
                            ))}
                          </div>
                        </div>
                      ))}
                      {d.notes && (
                        <Text size={200} className={styles.notes}>
                          {d.notes}
                        </Text>
                      )}
                      {d.checklist.length > 0 && (
                        <ul className={styles.checklist}>
                          {d.checklist.map((c) => (
                            <li key={c}>
                              <Text size={200}>{c}</Text>
                            </li>
                          ))}
                        </ul>
                      )}
                      <div className={styles.meta}>
                        {d.kind === "wiedervorlage" && (
                          <Badge appearance="tint" color="warning">
                            Wiedervorlage
                          </Badge>
                        )}
                        {d.dueTime && d.dueDate && <Badge appearance="tint">{d.dueTime} Uhr</Badge>}
                      </div>
                      {d.source && <Caption1 className={styles.source}>„{d.source}“</Caption1>}
                    </div>
                  );
                })}
                {drafts.length > 0 && (
                  <Caption1 className={styles.muted}>
                    Vorschläge von {modelLabel(model)}. Bitte kurz prüfen. Gelb markierte Felder waren unklar.
                  </Caption1>
                )}
              </>
            )}
    </>
  );

  const buttons = (
    <>
            {phase === "input" && (
              <>
                <Button onClick={close}>Abbrechen</Button>
                <Button appearance="primary" icon={<SparkleRegular />} disabled={(!text.trim() && !image) || preparing} onClick={() => void suggest()}>
                  Aufgaben vorschlagen
                </Button>
              </>
            )}
            {phase === "thinking" && <Button onClick={cancel}>Abbrechen</Button>}
            {phase === "review" && (
              <>
                <Button disabled={saving} onClick={() => setPhase("input")}>
                  Zurück
                </Button>
                {/* Ohne Vorschlag gibt es nichts anzulegen: dann nur „Zurück“, um den Text zu ergänzen */}
                {drafts.length > 0 && (
                  <Button appearance="primary" disabled={saving || chosen.length === 0} onClick={() => void create()}>
                    {saving ? "Lege an …" : chosen.length === 1 ? "Aufgabe anlegen" : `${chosen.length} Aufgaben anlegen`}
                  </Button>
                )}
              </>
            )}
    </>
  );

  // Als ganze Seite, im Fenster, das Teams für „Als Aufgabe“ öffnet
  if (inline) {
    return (
      <main className={styles.page}>
        <Text as="h1" size={500} weight="semibold">
          {heading}
        </Text>
        <div className={mergeClasses(styles.content, styles.pageBody)}>{body}</div>
        <div className={styles.pageActions}>{buttons}</div>
      </main>
    );
  }

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && close()}>
      <DialogSurface className={styles.surface}>
        <DialogBody>
          <DialogTitle action={<Button appearance="subtle" icon={<DismissRegular />} aria-label="Schließen" onClick={close} />}>{heading}</DialogTitle>
          <DialogContent className={styles.content}>{body}</DialogContent>
          <DialogActions>{buttons}</DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
