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
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  tokens,
} from "@fluentui/react-components";
import { CheckmarkCircleRegular, DismissRegular, PersonArrowRightRegular, SnoozeRegular } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { absenceProblem, decisionLabel, rangeLabel, reasonLabel, returnDay, suggestedAbsence, taskCount } from "../absence";
import { api } from "../api";
import { useApp } from "../context";
import { weekdayDate } from "../later";
import type { Absence, AbsenceDecision, AbsenceItem, UserRef } from "../types";
import { PersonPicker } from "./PersonPicker";

const useStyles = makeStyles({
  surface: { maxWidth: "640px", width: "calc(100vw - 32px)" },
  content: { display: "flex", flexDirection: "column", gap: "14px" },
  muted: { color: tokens.colorNeutralForeground3 },
  dates: { display: "flex", gap: "12px", flexWrap: "wrap", "& > *": { flex: "1 1 180px" } },
  head: {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    gap: "8px 12px",
    padding: "10px 12px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  headText: { flex: "1 1 220px", minWidth: 0, display: "flex", flexDirection: "column" },
  list: { display: "flex", flexDirection: "column", gap: "8px" },
  row: {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    gap: "6px 10px",
    padding: "10px 12px",
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  rowDecided: { backgroundColor: tokens.colorNeutralBackground2 },
  rowText: { flex: "1 1 220px", minWidth: 0, display: "flex", flexDirection: "column" },
  title: {
    textAlign: "left",
    border: "none",
    background: "transparent",
    padding: 0,
    fontFamily: "inherit",
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflowWrap: "anywhere",
  },
  link: { cursor: "pointer", ":hover": { textDecorationLine: "underline" } },
  danger: { color: tokens.colorPaletteRedForeground1 },
  note: { flex: "1 1 100%", minWidth: 0 },
  actions: { display: "flex", gap: "6px", flexWrap: "wrap", alignItems: "center" },
  decided: { display: "flex", alignItems: "center", gap: "6px", flex: "1 1 100%", color: tokens.colorNeutralForeground2 },
  decidedIcon: { fontSize: "18px", color: tokens.colorBrandForeground1, flexShrink: 0 },
  footer: { flexWrap: "wrap", rowGap: "8px" },
});

/** Urlaub eintragen, Aufgaben übergeben oder behalten, vorzeitig beenden oder löschen. */
export function AbsenceDialog({ onClose }: { onClose: () => void }) {
  const styles = useStyles();
  const status = useQuery({ queryKey: ["absences"], queryFn: api.absenceStatus });
  const current = status.data?.current ?? null;

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface className={styles.surface}>
        <DialogBody>
          <DialogTitle
            action={<Button appearance="subtle" aria-label="Schließen" icon={<DismissRegular />} onClick={onClose} />}
          >
            Urlaub und Vertretung
          </DialogTitle>
          {status.isLoading ? (
            <DialogContent>
              <Spinner />
            </DialogContent>
          ) : current ? (
            <Plan absence={current} onClose={onClose} />
          ) : (
            <NewAbsence onClose={onClose} />
          )}
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function NewAbsence({ onClose }: { onClose: () => void }) {
  const styles = useStyles();
  const { me, users, today, notify } = useApp();
  const queryClient = useQueryClient();
  const suggestion = suggestedAbsence(today);
  const [from, setFrom] = useState(suggestion.from);
  const [until, setUntil] = useState(suggestion.until);
  const [deputy, setDeputy] = useState<UserRef | null>(null);
  const [busy, setBusy] = useState(false);
  const problem = absenceProblem(from, until, deputy?.id ?? null, me.id, today);
  const dateProblem = from && until && until < from ? problem : null;

  const save = async () => {
    if (problem || !deputy) return;
    setBusy(true);
    try {
      await api.createAbsence({ from, until, deputyId: deputy.id });
      // Danach zeigt der Dialog gleich die Aufgaben, die zu entscheiden sind.
      await queryClient.invalidateQueries({ queryKey: ["absences"] });
    } catch (e) {
      notify(e instanceof Error ? e.message : "Urlaub konnte nicht eingetragen werden", "error");
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <DialogContent className={styles.content}>
        <div className={styles.dates}>
          <Field label="Erster freier Tag" required>
            <Input type="date" min={today} value={from} onChange={(_, d) => setFrom(d.value)} />
          </Field>
          <Field label="Letzter freier Tag" required validationState={dateProblem ? "error" : "none"} validationMessage={dateProblem ?? undefined}>
            <Input type="date" min={from || today} value={until} onChange={(_, d) => setUntil(d.value)} />
          </Field>
        </div>
        {from && until && !dateProblem && (
          <Caption1 className={styles.muted}>
            {rangeLabel(from, until)} · wieder da am {weekdayDate(returnDay(until))}
          </Caption1>
        )}
        <Field label="Vertretung" required hint="Bekommt die Aufgaben, die du übergibst, wie eine normale Zuweisung mit Benachrichtigung.">
          <PersonPicker users={users.filter((u) => u.id !== me.id)} value={deputy} onChange={setDeputy} placeholder="Name, E-Mail oder Funktion" />
        </Field>
        <Text className={styles.muted}>
          Danach siehst du alle deine Aufgaben, die bis zum Urlaubsende fällig werden oder schon überfällig sind, und alles, wo du nachhaken
          wolltest. Für jede entscheidest du: an die Vertretung übergeben oder bis nach dem Urlaub zurückstellen.
        </Text>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Abbrechen</Button>
        <Button appearance="primary" disabled={!!problem || busy} onClick={() => void save()}>
          Weiter
        </Button>
      </DialogActions>
    </>
  );
}

function Plan({ absence, onClose }: { absence: Absence; onClose: () => void }) {
  const styles = useStyles();
  const { notify, openTask, confirm } = useApp();
  const queryClient = useQueryClient();
  const plan = useQuery({ queryKey: ["absences", "plan", absence.id], queryFn: () => api.absencePlan(absence.id) });
  const [busy, setBusy] = useState(false);
  const items = plan.data?.items ?? [];
  const undecided = items.filter((i) => !i.decision).length;
  const deputyFirst = absence.deputy.name.split(" ")[0];

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["absences"] }),
      queryClient.invalidateQueries({ queryKey: ["tasks"] }),
      queryClient.invalidateQueries({ queryKey: ["counts"] }),
      queryClient.invalidateQueries({ queryKey: ["task"] }),
    ]);

  const run = async (work: () => Promise<string | null>) => {
    setBusy(true);
    try {
      const message = await work();
      if (message) notify(message);
      await refresh();
      return true;
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
      return false;
    } finally {
      setBusy(false);
    }
  };

  const handOverAll = () =>
    run(async () => {
      const { count } = await api.handOverAll(absence.id);
      return count === 0 ? "Nichts mehr zu übergeben" : `${taskCount(count)} an ${absence.deputy.name} übergeben`;
    });

  const end = async () => {
    const ok = await confirm({
      title: "Urlaub jetzt beenden?",
      text: "Behaltene Aufgaben sind sofort wieder da. Was bei der Vertretung liegt, holst du in „Mein Tag“ zurück.",
      confirmLabel: "Urlaub beenden",
    });
    if (!ok) return;
    if (await run(async () => (await api.endAbsence(absence.id), "Willkommen zurück!"))) onClose();
  };

  const remove = async () => {
    const text =
      absence.handedOver > 0
        ? `Was noch bei ${absence.deputy.name} liegt, kommt zu dir zurück, und Zurückgestelltes ist wieder wie vorher.`
        : "Zurückgestelltes ist wieder wie vorher.";
    if (!(await confirm({ title: "Urlaub löschen?", text, confirmLabel: "Urlaub löschen" }))) return;
    if (await run(async () => (await api.deleteAbsence(absence.id), "Urlaub gelöscht"))) onClose();
  };

  return (
    <>
      <DialogContent className={styles.content}>
        <div className={styles.head}>
          <div className={styles.headText}>
            <Text weight="semibold">
              {absence.started ? "Im Urlaub" : "Urlaub"} {rangeLabel(absence.from, absence.until)}
            </Text>
            <Caption1 className={styles.muted}>
              Vertretung: {absence.deputy.name} · wieder da am {weekdayDate(absence.returnDay)}
            </Caption1>
          </div>
          <div className={styles.actions}>
            {absence.started && (
              <Button size="small" disabled={busy} onClick={() => void end()}>
                Vorzeitig beenden
              </Button>
            )}
            <Button size="small" appearance="subtle" disabled={busy} onClick={() => void remove()}>
              Urlaub löschen
            </Button>
          </div>
        </div>

        {plan.isLoading && <Spinner />}
        {plan.error && (
          <MessageBar intent="error">
            <MessageBarBody>{plan.error instanceof Error ? plan.error.message : "Fehler beim Laden"}</MessageBarBody>
          </MessageBar>
        )}
        {plan.data && items.length === 0 && (
          <Text className={styles.muted}>Keine deiner Aufgaben wird bis zum Urlaubsende fällig, und du musst nirgends nachhaken. Schönen Urlaub!</Text>
        )}
        {items.length > 0 && (
          <Text className={styles.muted}>
            {undecided > 0
              ? `Noch ${taskCount(undecided)} zu entscheiden. Übergeben heißt: ${deputyFirst} wird verantwortlich und bekommt eine Benachrichtigung. Nach dem Urlaub holst du sie mit einem Klick zurück.`
              : "Alles entschieden. Du kannst den Dialog schließen, nach dem Urlaub zeigt „Mein Tag“, was passiert ist."}
          </Text>
        )}
        <div className={styles.list}>
          {items.map((i) => (
            <PlanRow
              key={i.taskId}
              absence={absence}
              item={i}
              disabled={busy}
              onDone={refresh}
              onOpen={() => {
                // Der Dialog liegt über allem, zum Ansehen schließen, er lässt sich jederzeit wieder öffnen.
                openTask(i.taskId);
                onClose();
              }}
            />
          ))}
        </div>
      </DialogContent>
      <DialogActions className={styles.footer}>
        <Button onClick={onClose}>Fertig</Button>
        <Button appearance="primary" icon={<PersonArrowRightRegular />} disabled={busy || undecided === 0} onClick={() => void handOverAll()}>
          {undecided === items.length ? "Alle übergeben" : `Übrige ${undecided} übergeben`}
        </Button>
      </DialogActions>
    </>
  );
}

interface RowProps {
  absence: Absence;
  item: AbsenceItem;
  disabled: boolean;
  onDone: () => Promise<unknown>;
  onOpen: () => void;
}

function PlanRow({ absence, item, disabled, onDone, onOpen }: RowProps) {
  const styles = useStyles();
  const { today, notify } = useApp();
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const reason = reasonLabel(item, absence.from, today);
  const deputyFirst = absence.deputy.name.split(" ")[0];
  // Liegt die übergebene Aufgabe inzwischen bei jemand anderem, gibt es hier nichts mehr zu entscheiden.
  const movedOn = item.decision === "uebergeben" && item.assignee.id !== absence.deputy.id;

  const decide = async (decision: AbsenceDecision) => {
    setBusy(true);
    try {
      await api.decideAbsenceTask(absence.id, item.taskId, decision, decision === "uebergeben" ? note.trim() : undefined);
      setNote("");
      await onDone();
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={mergeClasses(styles.row, !!item.decision && styles.rowDecided)}>
      <div className={styles.rowText}>
        {item.canOpen ? (
          <button type="button" className={mergeClasses(styles.title, styles.link)} onClick={onOpen}>
            {item.title}
          </button>
        ) : (
          <span className={styles.title}>{item.title}</span>
        )}
        {reason.text && <Caption1 className={reason.danger ? styles.danger : styles.muted}>{reason.text}</Caption1>}
      </div>
      {item.decision ? (
        <>
          <div className={styles.decided}>
            {item.decision === "uebergeben" ? <CheckmarkCircleRegular className={styles.decidedIcon} /> : <SnoozeRegular className={styles.decidedIcon} />}
            <Caption1>{decisionLabel(item, absence.deputy.id, absence.returnDay, today)}</Caption1>
          </div>
          {!movedOn && (
            <div className={styles.actions}>
              <Button
                size="small"
                appearance="subtle"
                disabled={disabled || busy}
                onClick={() => void decide(item.decision === "uebergeben" ? "behalten" : "uebergeben")}
              >
                {item.decision === "uebergeben" ? "Doch behalten" : `Doch an ${deputyFirst} übergeben`}
              </Button>
            </div>
          )}
        </>
      ) : (
        <>
          <Input
            className={styles.note}
            size="small"
            maxLength={300}
            placeholder={`Hinweis für ${deputyFirst} (optional)`}
            aria-label={`Hinweis für ${absence.deputy.name}`}
            value={note}
            onChange={(_, d) => setNote(d.value)}
            onKeyDown={(e) => e.key === "Enter" && !busy && void decide("uebergeben")}
          />
          <div className={styles.actions}>
            <Button size="small" appearance="primary" icon={<PersonArrowRightRegular />} disabled={disabled || busy} onClick={() => void decide("uebergeben")}>
              An {deputyFirst} übergeben
            </Button>
            <Button size="small" icon={<SnoozeRegular />} disabled={disabled || busy} onClick={() => void decide("behalten")}>
              Bleibt bei mir
            </Button>
          </div>
        </>
      )}
    </div>
  );
}
