import { useState } from "react";
import {
  Button,
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
  Switch,
  Text,
  tokens,
} from "@fluentui/react-components";
import { CopyRegular, SignOutRegular } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { accessActionLabel } from "../accessText";
import { useApp } from "../context";

const useStyles = makeStyles({
  content: { display: "flex", flexDirection: "column", gap: "18px", overflowX: "hidden" },
  muted: { color: tokens.colorNeutralForeground3 },
  row: { display: "flex", gap: "8px", minWidth: 0, marginTop: "8px", "& .fui-Input": { flex: 1, minWidth: 0 } },
});

export function SettingsDialog({ open, onClose, inTeams }: { open: boolean; onClose: () => void; inTeams: boolean }) {
  const styles = useStyles();
  const { me, notify } = useApp();
  const queryClient = useQueryClient();
  const [feedUrl, setFeedUrl] = useState<string | null>(null);
  const aboutMe = useQuery({ queryKey: ["access-about-me"], queryFn: api.accessAboutMe, enabled: open });

  const update = async (body: Parameters<typeof api.updateMe>[0]) => {
    try {
      await api.updateMe(body);
      await queryClient.invalidateQueries({ queryKey: ["me"] });
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  const createFeed = async () => {
    try {
      const { url } = await api.createCalendarFeed();
      setFeedUrl(url);
      await queryClient.invalidateQueries({ queryKey: ["me"] });
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  const removeFeed = async () => {
    await api.deleteCalendarFeed();
    setFeedUrl(null);
    await queryClient.invalidateQueries({ queryKey: ["me"] });
    notify("Kalender-Link deaktiviert");
  };

  return (
    <Dialog open={open} onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Einstellungen</DialogTitle>
          <DialogContent className={styles.content}>
            <Field label="Benachrichtigungen" hint="„Automatisch“ nutzt Teams, sobald du die App einmal in Teams geöffnet hast – sonst E-Mail.">
              <RadioGroup value={me.notifyChannel} onChange={(_, d) => update({ notifyChannel: d.value as typeof me.notifyChannel })}>
                <Radio value="auto" label="Automatisch" />
                <Radio value="teams" label="Teams-Aktivitätsfeed" disabled={!me.teamsNotificationsAvailable} />
                <Radio value="email" label="E-Mail" disabled={!me.mailAvailable} />
                <Radio value="aus" label="Keine Benachrichtigungen" />
              </RadioGroup>
            </Field>

            <Switch
              checked={me.digestEnabled}
              label={me.digestOnWeekends ? "Tagesübersicht am Morgen" : "Tagesübersicht am Morgen (werktags)"}
              onChange={(_, d) => update({ digestEnabled: d.checked })}
            />

            {me.mailInboxAddress && (
              <Field label="Mail an Aufgabe">
                <Text className={styles.muted}>
                  Leite eine E-Mail an diese Adresse weiter – daraus wird eine Aufgabe mit Text und Anhängen. Im Betreff funktioniert dasselbe wie in der
                  Schnelleingabe: #Projekt, @Person, !hoch, „wv“, heute, morgen oder ein Datum wie 30.11.
                </Text>
                <div className={styles.row}>
                  <Input readOnly value={me.mailInboxAddress} onFocus={(e) => e.currentTarget.select()} />
                  <Button
                    icon={<CopyRegular />}
                    onClick={() => navigator.clipboard.writeText(me.mailInboxAddress ?? "").then(() => notify("Adresse kopiert"))}
                  >
                    Kopieren
                  </Button>
                </div>
              </Field>
            )}

            <Field label="Wer darf meine Aufgaben sehen?">
              {aboutMe.data && aboutMe.data.grants.length === 0 && (
                <Text className={styles.muted}>Niemand – deine persönlichen Aufgaben siehst nur du.</Text>
              )}
              {aboutMe.data?.grants.map((g) => (
                <Text key={g.id} block>
                  <b>{g.viewer.name}</b> · {g.permission === "bearbeiten" ? "lesen und bearbeiten" : "nur lesen"} ·{" "}
                  {g.status === "entwurf" ? "wartet auf Unterschrift" : g.validUntil ? `bis ${g.validUntil.split("-").reverse().join(".")}` : "bis zum Widerruf"}
                  {" · "}
                  {g.purpose}
                </Text>
              ))}
              {aboutMe.data && aboutMe.data.log.length > 0 && (
                <div style={{ marginTop: 6 }}>
                  <Text size={200} className={styles.muted} block>
                    Letzte Zugriffe:
                  </Text>
                  {aboutMe.data.log.slice(0, 8).map((l) => (
                    <Text key={l.id} size={200} className={styles.muted} block>
                      {new Date(l.at).toLocaleString("de-DE", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })} ·{" "}
                      {l.viewer.name} {accessActionLabel(l.action)}
                    </Text>
                  ))}
                </div>
              )}
            </Field>

            <Field label="Aufgaben im Outlook-Kalender">
              <Text className={styles.muted}>
                Ein persönlicher Link, den Outlook als zusätzlichen Kalender abonniert. Aufgaben mit Termin erscheinen dort – getrennt von deinen echten Terminen. Der Link ist geheim; wer ihn hat, sieht deine Aufgaben mit Termin.
              </Text>
              {feedUrl ? (
                <>
                  <div className={styles.row}>
                    <Input readOnly value={feedUrl} onFocus={(e) => e.currentTarget.select()} />
                    <Button
                      icon={<CopyRegular />}
                      onClick={() => navigator.clipboard.writeText(feedUrl).then(() => notify("Link kopiert"))}
                    >
                      Kopieren
                    </Button>
                  </div>
                  <MessageBar intent="info" layout="multiline">
                    <MessageBarBody>
                      Outlook: Kalender → Kalender hinzufügen → „Aus dem Internet“ → Link einfügen. Der Link wird nur jetzt angezeigt.
                    </MessageBarBody>
                  </MessageBar>
                </>
              ) : (
                <div className={styles.row}>
                  <Button onClick={createFeed}>{me.hasCalendarFeed ? "Neuen Link erzeugen" : "Kalender-Link erzeugen"}</Button>
                  {me.hasCalendarFeed && (
                    <Button appearance="subtle" onClick={removeFeed}>
                      Link deaktivieren
                    </Button>
                  )}
                </div>
              )}
              {me.hasCalendarFeed && !feedUrl && <Text size={200} className={styles.muted}>Ein neuer Link macht den alten ungültig.</Text>}
            </Field>
          </DialogContent>
          <DialogActions>
            {!inTeams && (
              <Button
                appearance="subtle"
                icon={<SignOutRegular />}
                onClick={async () => {
                  await api.logout();
                  window.location.reload();
                }}
              >
                Abmelden
              </Button>
            )}
            <Button appearance="primary" onClick={onClose}>
              Fertig
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
