import { useState } from "react";
import {
  Button,
  Caption1,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  makeStyles,
  mergeClasses,
  Radio,
  RadioGroup,
  Text,
  tokens,
} from "@fluentui/react-components";
import { CheckmarkRegular, EditRegular, HourglassHalfRegular, SnoozeRegular } from "@fluentui/react-icons";
import { useApp } from "../context";
import { relativeDay } from "../dates";
import { addDays, defaultFollowUp, followUpChoices, followUpDue, laterAction, sinceLabel, weekdayDate } from "../later";
import type { Task } from "../types";
import { LaterMenu } from "./LaterMenu";

const useStyles = makeStyles({
  buttons: { display: "flex", gap: "8px", flexWrap: "wrap" },
  box: {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    gap: "8px 12px",
    padding: "10px 12px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    borderLeft: `3px solid ${tokens.colorBrandStroke1}`,
  },
  boxDue: { backgroundColor: tokens.colorPaletteDarkOrangeBackground1, borderLeftColor: tokens.colorPaletteDarkOrangeBorder2 },
  icon: { fontSize: "20px", color: tokens.colorBrandForeground1, flexShrink: 0 },
  iconDue: { color: tokens.colorPaletteDarkOrangeForeground1 },
  text: { flex: "1 1 160px", minWidth: 0, display: "flex", flexDirection: "column" },
  muted: { color: tokens.colorNeutralForeground3 },
  actions: { display: "flex", gap: "6px", flexWrap: "wrap" },
  form: { display: "flex", flexDirection: "column", gap: "14px" },
});

type Patch = (body: Record<string, unknown>, success?: string) => Promise<boolean>;

/**
 * „Später“ und „Warte auf …“ in der geöffneten Aufgabe.
 * readOnly: Wer die Aufgabe nicht selbst plant (z. B. delegiert), sieht nur den Stand.
 */
export function LaterPanel({ task, patch, readOnly }: { task: Task; patch: Patch; readOnly: boolean }) {
  const styles = useStyles();
  const { today } = useApp();
  const [dialog, setDialog] = useState(false);
  const due = followUpDue(task, today);

  if (task.waitingFor) {
    const details = [
      sinceLabel(task.waitingSince, today),
      task.snoozedUntil && (due ? `nachhaken seit ${weekdayDate(task.snoozedUntil)}` : `nachhaken ${relativeDay(task.snoozedUntil, today)}`),
    ].filter(Boolean);
    return (
      <div className={mergeClasses(styles.box, due && styles.boxDue)}>
        <HourglassHalfRegular className={mergeClasses(styles.icon, due && styles.iconDue)} />
        <div className={styles.text}>
          <Text weight="semibold">{due ? `Nachhaken bei ${task.waitingFor}` : `Warte auf ${task.waitingFor}`}</Text>
          <Caption1 className={styles.muted}>{details.join(" · ")}</Caption1>
        </div>
        {!readOnly && (
          <div className={styles.actions}>
            <Button size="small" appearance="primary" icon={<CheckmarkRegular />} onClick={() => patch({ waitingFor: null }, "Antwort ist da – die Aufgabe steht jetzt in „Mein Tag“")}>
              Antwort da
            </Button>
            <LaterMenu
              choices={followUpChoices(today)}
              title="Wann nachhaken?"
              trigger={
                <Button size="small" icon={<SnoozeRegular />}>
                  {due ? "Später nachhaken" : "Anderer Tag"}
                </Button>
              }
              onPick={(date) => patch({ snoozedUntil: date }, `Nachhaken am ${weekdayDate(date)}`)}
            />
            <Button size="small" appearance="subtle" icon={<EditRegular />} aria-label="„Warte auf“ ändern" onClick={() => setDialog(true)} />
          </div>
        )}
        {dialog && <WaitDialog task={task} patch={patch} onClose={() => setDialog(false)} />}
      </div>
    );
  }

  if (task.isSnoozed && task.snoozedUntil) {
    return (
      <div className={styles.box}>
        <SnoozeRegular className={styles.icon} />
        <div className={styles.text}>
          <Text weight="semibold">Zurückgestellt bis {weekdayDate(task.snoozedUntil)}</Text>
          <Caption1 className={styles.muted}>
            {task.dueDate && task.dueDate < task.snoozedUntil
              ? `Wegen der Fälligkeit schon am ${weekdayDate(task.dueDate)} wieder da.`
              : "Taucht dann oben in „Mein Tag“ wieder auf."}
          </Caption1>
        </div>
        {!readOnly && (
          <div className={styles.actions}>
            <Button size="small" appearance="primary" onClick={() => patch({ snoozedUntil: null }, "Wieder da – steht jetzt in „Mein Tag“")}>
              Jetzt zurückholen
            </Button>
            <LaterMenu
              trigger={
                <Button size="small" icon={<SnoozeRegular />}>
                  Anderer Tag
                </Button>
              }
              onPick={(date) => patch({ snoozedUntil: date }, `Zurückgestellt bis ${weekdayDate(date)}`)}
            />
          </div>
        )}
      </div>
    );
  }

  if (readOnly) return null;
  return (
    <div className={styles.buttons}>
      <LaterMenu
        trigger={<Button icon={<SnoozeRegular />}>Später</Button>}
        onPick={(date) => {
          const action = laterAction(task, date, today);
          void patch(action.patch, action.message);
        }}
      />
      <Button icon={<HourglassHalfRegular />} onClick={() => setDialog(true)}>
        Warte auf …
      </Button>
      {dialog && <WaitDialog task={task} patch={patch} onClose={() => setDialog(false)} />}
    </div>
  );
}

/** „Warte auf …“ festlegen oder ändern: auf wen, und wann nachhaken. */
function WaitDialog({ task, patch, onClose }: { task: Task; patch: Patch; onClose: () => void }) {
  const styles = useStyles();
  const { today, users, me } = useApp();
  const choices = followUpChoices(today);
  const initial = task.waitingFor && task.snoozedUntil && task.snoozedUntil > today ? task.snoozedUntil : defaultFollowUp(today);
  const [name, setName] = useState(task.waitingFor);
  const [pick, setPick] = useState(choices.some((c) => c.date === initial) ? initial : "datum");
  const [custom, setCustom] = useState(initial);
  const [busy, setBusy] = useState(false);
  const date = pick === "datum" ? custom : pick;
  const trimmed = name.trim();
  const valid = trimmed.length > 0 && trimmed.length <= 200 && !!date && date > today;

  const save = async () => {
    setBusy(true);
    const body: Record<string, unknown> = { waitingFor: trimmed };
    // Wird nur der Name korrigiert, bleibt der Tag zum Nachhaken, wie er ist.
    if (!task.waitingFor || date !== task.snoozedUntil) body.snoozedUntil = date;
    const ok = await patch(body, `Warte auf ${trimmed} – nachhaken am ${weekdayDate(date)}`);
    setBusy(false);
    if (ok) onClose();
  };

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface style={{ maxWidth: 440 }}>
        <DialogBody>
          <DialogTitle>Warte auf …</DialogTitle>
          <DialogContent className={styles.form}>
            <Field label="Auf wen oder was wartest du?" required hint="z. B. Fa. Müller, Mieterin Schmidt, Angebot Dachdecker">
              <Input
                autoFocus
                list="aufgaben-personen"
                maxLength={200}
                value={name}
                onChange={(_, d) => setName(d.value)}
                onKeyDown={(e) => e.key === "Enter" && valid && !busy && void save()}
              />
            </Field>
            <datalist id="aufgaben-personen">
              {users
                .filter((u) => u.id !== me.id)
                .map((u) => (
                  <option key={u.id} value={u.name} />
                ))}
            </datalist>
            <Field label="Nachhaken">
              <RadioGroup value={pick} onChange={(_, d) => setPick(d.value)}>
                {choices.map((c) => (
                  <Radio key={c.date} value={c.date} label={`${c.label} (${weekdayDate(c.date)})`} />
                ))}
                <Radio value="datum" label="Anderer Tag" />
              </RadioGroup>
            </Field>
            {pick === "datum" && (
              <Field label="Tag zum Nachhaken">
                <Input type="date" min={addDays(today, 1)} value={custom} onChange={(_, d) => setCustom(d.value)} />
              </Field>
            )}
            <Caption1 className={styles.muted}>
              Bis dahin ist die Aufgabe aus deinen Listen raus und steht in der „Warteschleife“. Am Tag zum Nachhaken ist sie wieder in
              „Mein Tag“, und du bekommst eine Erinnerung.
              {task.dueDate && date && task.dueDate < date && task.dueDate > today ? ` Wegen der Fälligkeit am ${weekdayDate(task.dueDate)} ist sie schon früher wieder da.` : ""}
            </Caption1>
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Abbrechen</Button>
            <Button appearance="primary" disabled={!valid || busy} onClick={() => void save()}>
              Speichern
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
