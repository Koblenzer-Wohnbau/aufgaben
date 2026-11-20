import { useState } from "react";
import {
  Avatar,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  makeStyles,
  Text,
  tokens,
} from "@fluentui/react-components";
import { DeleteRegular, DoorArrowLeftRegular } from "@fluentui/react-icons";
import { useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { initials, useApp } from "../context";
import { navigate } from "../route";
import type { TaskList, UserRef } from "../types";
import { PersonPicker } from "./PersonPicker";

const useStyles = makeStyles({
  content: { display: "flex", flexDirection: "column", gap: "16px" },
  muted: { color: tokens.colorNeutralForeground3 },
  person: { display: "flex", alignItems: "center", gap: "10px", padding: "6px 0" },
  name: { flex: 1 },
});

/** Liste teilen: Mitglieder sehen alle Aufgaben der Liste und können selbst welche anlegen. */
export function ShareListDialog({ list, onClose }: { list: TaskList; onClose: () => void }) {
  const styles = useStyles();
  const { users, me, notify } = useApp();
  const queryClient = useQueryClient();
  const [pick, setPick] = useState<UserRef | null>(null);

  const refresh = () =>
    Promise.all([queryClient.invalidateQueries({ queryKey: ["lists"] }), queryClient.invalidateQueries({ queryKey: ["tasks"] })]);

  const add = async (user: UserRef | null) => {
    setPick(null);
    if (!user) return;
    try {
      await api.addListMember(list.id, user.id);
      await refresh();
      notify(`Mit ${user.name} geteilt – ${user.name.split(" ")[0]} wird benachrichtigt`);
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  const remove = async (user: UserRef) => {
    try {
      await api.removeListMember(list.id, user.id);
      await refresh();
      if (user.id === me.id) {
        notify("Du hast die Liste verlassen");
        onClose();
        navigate({ page: "tasks", view: "mein-tag" });
      }
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    }
  };

  const candidates = users.filter((u) => u.id !== list.owner.id && !list.members.some((m) => m.id === u.id));

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>„{list.name}“ teilen</DialogTitle>
          <DialogContent className={styles.content}>
            <Text className={styles.muted}>
              Alle Mitglieder sehen sämtliche Aufgaben dieser Liste und können eigene hinzufügen. Aufgaben außerhalb der Liste bleiben privat.
            </Text>
            {list.isOwner && (
              <Field label="Person hinzufügen">
                <PersonPicker users={candidates} value={pick} onChange={(u) => void add(u)} placeholder="Name oder E-Mail" />
              </Field>
            )}
            <div>
              <div className={styles.person}>
                <Avatar name={list.owner.name} initials={initials(list.owner.name)} color="colorful" />
                <Text className={styles.name}>{list.owner.name}</Text>
                <Text className={styles.muted}>Besitzer</Text>
              </div>
              {list.members.map((m) => (
                <div key={m.id} className={styles.person}>
                  <Avatar name={m.name} initials={initials(m.name)} color="colorful" />
                  <Text className={styles.name}>{m.name}</Text>
                  {list.isOwner && (
                    <Button appearance="subtle" icon={<DeleteRegular />} aria-label={`${m.name} entfernen`} onClick={() => void remove(m)} />
                  )}
                </div>
              ))}
              {list.members.length === 0 && <Text className={styles.muted}>Noch mit niemandem geteilt.</Text>}
            </div>
          </DialogContent>
          <DialogActions>
            {!list.isOwner && (
              <Button icon={<DoorArrowLeftRegular />} onClick={() => void remove(users.find((u) => u.id === me.id) ?? { id: me.id, name: me.name, email: me.email, jobTitle: null })}>
                Liste verlassen
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
