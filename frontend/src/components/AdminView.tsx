import { useState } from "react";
import {
  Badge,
  Button,
  Caption1,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Radio,
  RadioGroup,
  Spinner,
  Tab,
  TabList,
  Text,
  Textarea,
  Title2,
  tokens,
} from "@fluentui/react-components";
import {
  AddRegular,
  ArrowDownloadRegular,
  ArrowSyncRegular,
  CheckmarkRegular,
  DeleteRegular,
  HistoryRegular,
  MailInboxRegular,
  PrintRegular,
  ProhibitedRegular,
  ShieldPersonRegular,
} from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { accessActionLabel } from "../accessText";
import { printConsentForm } from "../consentForm";
import { useApp } from "../context";
import { shortDateTime } from "../dates";
import type { AccessGrant, UserRef } from "../types";
import { PersonPicker } from "./PersonPicker";

const useStyles = makeStyles({
  view: { flex: 1, overflowY: "auto", padding: "20px 32px 48px" },
  header: { display: "flex", alignItems: "center", gap: "12px", marginBottom: "4px" },
  icon: { fontSize: "28px", color: tokens.colorBrandForeground1, display: "flex" },
  muted: { color: tokens.colorNeutralForeground3 },
  toolbar: { display: "flex", gap: "8px", margin: "16px 0", flexWrap: "wrap", alignItems: "center" },
  grid: { display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(340px, 1fr))", gap: "12px" },
  card: { padding: "14px 16px", display: "flex", flexDirection: "column", gap: "6px" },
  row: { display: "flex", alignItems: "center", gap: "8px", flexWrap: "wrap" },
  arrow: { color: tokens.colorNeutralForeground3 },
  actions: { display: "flex", gap: "6px", flexWrap: "wrap", marginTop: "6px" },
  form: { display: "flex", flexDirection: "column", gap: "14px" },
  two: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" },
  stat: { padding: "14px 16px" },
  statValue: { fontSize: "24px", fontWeight: tokens.fontWeightSemibold },
  log: { display: "flex", flexDirection: "column", gap: "6px", maxHeight: "50vh", overflowY: "auto" },
});


function de(date: string | null) {
  if (!date) return "";
  const [y, m, d] = date.split("-");
  return `${d}.${m}.${y}`;
}

function statusBadge(g: AccessGrant) {
  if (g.status === "entwurf") return <Badge appearance="tint" color="warning">Entwurf – wartet auf Unterschrift</Badge>;
  if (g.status === "widerrufen") return <Badge appearance="tint" color="subtle">Widerrufen</Badge>;
  return g.isCurrentlyActive ? <Badge appearance="tint" color="success">Aktiv</Badge> : <Badge appearance="tint" color="informative">Bestätigt, nicht im Zeitraum</Badge>;
}

export function AdminView() {
  const styles = useStyles();
  const { me } = useApp();
  const [tab, setTab] = useState<"einsicht" | "system">("einsicht");

  if (!me.isAdmin) {
    return (
      <div className={styles.view}>
        <MessageBar intent="warning">
          <MessageBarBody>Dieser Bereich ist nur für die Verwaltung (App-Rolle Aufgaben.Admin).</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  return (
    <div className={styles.view}>
      <div className={styles.header}>
        <span className={styles.icon}>
          <ShieldPersonRegular />
        </span>
        <Title2 as="h1">Verwaltung</Title2>
      </div>
      <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as "einsicht" | "system")}>
        <Tab value="einsicht">Einsichtsrechte</Tab>
        <Tab value="system">System</Tab>
      </TabList>
      {tab === "einsicht" ? <AccessSection /> : <SystemSection />}
    </div>
  );
}

function AccessSection() {
  const styles = useStyles();
  const { notify } = useApp();
  const queryClient = useQueryClient();
  const grants = useQuery({ queryKey: ["admin-access"], queryFn: api.adminAccess });
  const [creating, setCreating] = useState(false);
  const [confirming, setConfirming] = useState<AccessGrant | null>(null);
  const [revoking, setRevoking] = useState<AccessGrant | null>(null);
  const [logFor, setLogFor] = useState<AccessGrant | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["admin-access"] });

  const print = (g: AccessGrant) => {
    if (!printConsentForm(g)) notify("Das Druckfenster wurde blockiert – bitte Pop-ups für diese Seite erlauben.", "error");
  };

  const remove = async (g: AccessGrant) => {
    if (!window.confirm(`Entwurf für ${g.owner.name} → ${g.viewer.name} löschen?`)) return;
    try {
      await api.deleteAccess(g.id);
      await refresh();
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  return (
    <>
      <Text block className={styles.muted} style={{ marginTop: 12 }}>
        Eine Person darf die Aufgaben einer anderen nur sehen, wenn hier eine Freigabe angelegt, das Formular unterschrieben und die Unterschrift
        bestätigt ist. Beide Personen werden benachrichtigt (je nach ihrer Einstellung), jeder Zugriff wird protokolliert.
      </Text>
      <MessageBar intent="info" style={{ marginTop: 12 }}>
        <MessageBarBody>
          Das Formular ist eine Vorlage. Bitte vor der ersten Verwendung von der/dem Datenschutzbeauftragten prüfen lassen bzw. per Betriebsvereinbarung regeln.
        </MessageBarBody>
      </MessageBar>
      <div className={styles.toolbar}>
        <Button appearance="primary" icon={<AddRegular />} onClick={() => setCreating(true)}>
          Neue Freigabe
        </Button>
      </div>
      {grants.isLoading && <Spinner />}
      {grants.data?.length === 0 && <Text className={styles.muted}>Noch keine Freigaben.</Text>}
      <div className={styles.grid}>
        {grants.data?.map((g) => (
          <Card key={g.id} className={styles.card}>
            <div className={styles.row}>
              <Text weight="semibold">{g.viewer.name}</Text>
              <Text className={styles.arrow}>sieht die Aufgaben von</Text>
              <Text weight="semibold">{g.owner.name}</Text>
            </div>
            <div className={styles.row}>
              {statusBadge(g)}
              <Badge appearance="outline">{g.permission === "bearbeiten" ? "lesen und bearbeiten" : "nur lesen"}</Badge>
            </div>
            <Caption1 className={styles.muted}>
              {g.purpose} · ab {de(g.validFrom)} {g.validUntil ? `bis ${de(g.validUntil)}` : "unbefristet"} · Nr. {g.id}
            </Caption1>
            {g.confirmedAt && (
              <Caption1 className={styles.muted}>
                Unterschrift bestätigt {shortDateTime(g.confirmedAt)} · Original: {g.consentNote}
              </Caption1>
            )}
            {g.revokedAt && (
              <Caption1 className={styles.muted}>
                Widerrufen {shortDateTime(g.revokedAt)} · {g.revokeReason}
              </Caption1>
            )}
            <div className={styles.actions}>
              <Button size="small" icon={<PrintRegular />} onClick={() => print(g)}>
                Formular
              </Button>
              {g.status === "entwurf" && (
                <Button size="small" appearance="primary" icon={<CheckmarkRegular />} onClick={() => setConfirming(g)}>
                  Unterschrift bestätigen
                </Button>
              )}
              {g.status !== "widerrufen" && (
                <Button size="small" icon={<ProhibitedRegular />} onClick={() => setRevoking(g)}>
                  Widerrufen
                </Button>
              )}
              {(g.status !== "entwurf" || g.accessCount > 0) && (
                <Button size="small" icon={<HistoryRegular />} onClick={() => setLogFor(g)}>
                  Protokoll ({g.accessCount})
                </Button>
              )}
              {g.status === "entwurf" && (
                <Button size="small" appearance="subtle" icon={<DeleteRegular />} onClick={() => void remove(g)} aria-label="Entwurf löschen" />
              )}
            </div>
          </Card>
        ))}
      </div>
      {creating && (
        <CreateGrantDialog
          onClose={() => setCreating(false)}
          onCreated={async (g) => {
            setCreating(false);
            await refresh();
            print(g);
          }}
        />
      )}
      {confirming && (
        <DecisionDialog
          title="Unterschrift bestätigen"
          text={`Liegt das von ${confirming.owner.name}, ${confirming.viewer.name} und der Verwaltung unterschriebene Formular vor? Erst danach wird die Einsicht wirksam.`}
          label="Wo liegt das Original?"
          placeholder="z. B. Personalakte"
          action="Bestätigen und aktivieren"
          onClose={() => setConfirming(null)}
          onSubmit={async (note) => {
            await api.confirmAccess(confirming.id, note);
            setConfirming(null);
            await refresh();
            notify("Freigabe aktiv – beide Personen werden benachrichtigt (je nach ihrer Einstellung)");
          }}
        />
      )}
      {revoking && (
        <DecisionDialog
          title="Freigabe widerrufen"
          text={`${revoking.viewer.name} kann die Aufgaben von ${revoking.owner.name} danach sofort nicht mehr sehen.`}
          label="Grund"
          placeholder="z. B. Vertretung beendet, Widerruf durch die Person"
          action="Widerrufen"
          optional
          onClose={() => setRevoking(null)}
          onSubmit={async (note) => {
            await api.revokeAccess(revoking.id, note);
            setRevoking(null);
            await refresh();
            notify("Freigabe widerrufen");
          }}
        />
      )}
      {logFor && <LogDialog grant={logFor} onClose={() => setLogFor(null)} />}
    </>
  );
}

function CreateGrantDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (g: AccessGrant) => void }) {
  const styles = useStyles();
  const { users, today, notify } = useApp();
  const [owner, setOwner] = useState<UserRef | null>(null);
  const [viewer, setViewer] = useState<UserRef | null>(null);
  const [permission, setPermission] = useState<"lesen" | "bearbeiten">("lesen");
  const [from, setFrom] = useState(today);
  const [until, setUntil] = useState("");
  const [purpose, setPurpose] = useState("");
  const [busy, setBusy] = useState(false);

  const valid = owner && viewer && owner.id !== viewer.id && purpose.trim().length >= 3 && from && (!until || until >= from);

  const submit = async () => {
    if (!owner || !viewer) return;
    setBusy(true);
    try {
      const g = await api.createAccess({ ownerId: owner.id, viewerId: viewer.id, permission, validFrom: from, validUntil: until || null, purpose: purpose.trim() });
      onCreated(g);
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Neue Freigabe</DialogTitle>
          <DialogContent className={styles.form}>
            <Field label="Wessen Aufgaben?" required>
              <PersonPicker users={users} value={owner} onChange={setOwner} />
            </Field>
            <Field label="Wer erhält Einsicht?" required validationMessage={owner && viewer && owner.id === viewer.id ? "Bitte zwei verschiedene Personen wählen." : undefined}>
              <PersonPicker users={users} value={viewer} onChange={setViewer} />
            </Field>
            <Field label="Umfang">
              <RadioGroup value={permission} onChange={(_, d) => setPermission(d.value as "lesen" | "bearbeiten")} layout="horizontal">
                <Radio value="lesen" label="Nur lesen" />
                <Radio value="bearbeiten" label="Lesen und bearbeiten" />
              </RadioGroup>
            </Field>
            <div className={styles.two}>
              <Field label="Ab">
                <Input type="date" value={from} onChange={(_, d) => setFrom(d.value)} />
              </Field>
              <Field label="Bis (optional)" hint="Leer = bis zum Widerruf">
                <Input type="date" value={until} onChange={(_, d) => setUntil(d.value)} />
              </Field>
            </div>
            <Field label="Zweck" required hint="Steht auf dem Formular, z. B. „Urlaubsvertretung 22.12.–06.01.“">
              <Input value={purpose} onChange={(_, d) => setPurpose(d.value)} maxLength={300} />
            </Field>
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Abbrechen</Button>
            <Button appearance="primary" icon={<PrintRegular />} disabled={!valid || busy} onClick={() => void submit()}>
              Anlegen und Formular drucken
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function DecisionDialog(props: {
  title: string;
  text: string;
  label: string;
  placeholder: string;
  action: string;
  optional?: boolean;
  onClose: () => void;
  onSubmit: (note: string) => Promise<void>;
}) {
  const styles = useStyles();
  const { notify } = useApp();
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const valid = props.optional || note.trim().length >= 3;
  return (
    <Dialog open onOpenChange={(_, d) => !d.open && props.onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{props.title}</DialogTitle>
          <DialogContent className={styles.form}>
            <Text>{props.text}</Text>
            <Field label={props.label} required={!props.optional}>
              <Textarea value={note} placeholder={props.placeholder} onChange={(_, d) => setNote(d.value)} maxLength={300} />
            </Field>
          </DialogContent>
          <DialogActions>
            <Button onClick={props.onClose}>Abbrechen</Button>
            <Button
              appearance="primary"
              disabled={!valid || busy}
              onClick={async () => {
                setBusy(true);
                try {
                  await props.onSubmit(note.trim());
                } catch (e) {
                  notify(e instanceof Error ? e.message : "Fehler", "error");
                } finally {
                  setBusy(false);
                }
              }}
            >
              {props.action}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function LogDialog({ grant, onClose }: { grant: AccessGrant; onClose: () => void }) {
  const styles = useStyles();
  const log = useQuery({ queryKey: ["access-log", grant.id], queryFn: () => api.accessLog(grant.id) });
  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>
            Protokoll: {grant.viewer.name} → {grant.owner.name}
          </DialogTitle>
          <DialogContent>
            {log.isLoading && <Spinner />}
            <div className={styles.log}>
              {log.data?.length === 0 && <Text className={styles.muted}>Noch keine Zugriffe.</Text>}
              {log.data?.map((l) => (
                <Text key={l.id}>
                  <Caption1 className={styles.muted}>{shortDateTime(l.at)}</Caption1> · <b>{l.viewer.name}</b> {accessActionLabel(l.action)}
                  {l.taskId ? ` (Aufgabe #${l.taskId})` : ""}
                </Text>
              ))}
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" onClick={onClose}>
              Schließen
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function SystemSection() {
  const styles = useStyles();
  const { notify } = useApp();
  const status = useQuery({ queryKey: ["admin-status"], queryFn: api.adminStatus });
  const [busy, setBusy] = useState<string | null>(null);

  const run = async (key: string, fn: () => Promise<unknown>, done: (r: unknown) => string) => {
    setBusy(key);
    try {
      const r = await fn();
      notify(done(r));
      await status.refetch();
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    } finally {
      setBusy(null);
    }
  };

  const s = status.data;
  const yes = (v: boolean) => (v ? <Badge color="success">eingerichtet</Badge> : <Badge color="subtle">nicht eingerichtet</Badge>);

  return (
    <>
      <div className={styles.toolbar}>
        <Button
          icon={<ArrowSyncRegular />}
          disabled={!s?.graphConfigured || busy !== null}
          onClick={() => void run("sync", api.directorySync, (r) => {
            const x = r as { added: number; updated: number; deactivated: number };
            return `Verzeichnis: ${x.added} neu, ${x.updated} aktualisiert, ${x.deactivated} deaktiviert`;
          })}
        >
          Verzeichnis jetzt abgleichen
        </Button>
        <Button
          icon={<MailInboxRegular />}
          disabled={!s?.mailInboxConfigured || busy !== null}
          onClick={() => void run("inbox", api.pollInbox, (r) => `Postfach: ${(r as { result: string }).result}`)}
        >
          Aufgaben-Postfach jetzt abrufen
        </Button>
        <Button
          icon={<ArrowDownloadRegular />}
          disabled={busy !== null}
          onClick={() => void run("teams", api.downloadTeamsPackage, (name) =>
            `${name} gespeichert – im Teams Admin Center unter „Apps verwalten“ hochladen.`)}
        >
          Teams-Paket herunterladen
        </Button>
      </div>
      {status.isLoading && <Spinner />}
      {s && (
        <div className={styles.grid}>
          <Card className={styles.stat}>
            <Caption1 className={styles.muted}>Aktive Personen</Caption1>
            <span className={styles.statValue}>{s.activeUsers}</span>
          </Card>
          <Card className={styles.stat}>
            <Caption1 className={styles.muted}>Version</Caption1>
            <span className={styles.statValue}>{s.version}</span>
          </Card>
          <Card className={styles.stat}>
            <Caption1 className={styles.muted}>Benachrichtigungen</Caption1>
            <span className={styles.statValue}>{s.pendingNotifications} offen</span>
            {s.failedNotifications > 0 && <Badge color="danger">{s.failedNotifications} endgültig fehlgeschlagen</Badge>}
          </Card>
          <Card className={styles.stat}>
            <Text weight="semibold">Microsoft Graph {yes(s.graphConfigured)}</Text>
            <Caption1 className={styles.muted}>
              Teams-Benachrichtigungen {s.teamsNotificationsConfigured ? "aktiv" : "aus"} · letzter Verzeichnisabgleich{" "}
              {s.lastDirectorySync ? shortDateTime(s.lastDirectorySync) : "–"} {s.lastDirectorySyncResult ? `(${s.lastDirectorySyncResult})` : ""}
            </Caption1>
          </Card>
          <Card className={styles.stat}>
            <Text weight="semibold">E-Mail {yes(s.mailConfigured)}</Text>
            <Caption1 className={styles.muted}>
              Aufgaben-Postfach {s.mailInboxConfigured ? "aktiv" : "aus"} · letzter Abruf {s.lastMailInboxRun ? shortDateTime(s.lastMailInboxRun) : "–"}{" "}
              {s.lastMailInboxResult ? `(${s.lastMailInboxResult})` : ""} · abgelehnt (7 Tage): {s.rejectedMailsLast7Days}
            </Caption1>
          </Card>
        </div>
      )}
    </>
  );
}
