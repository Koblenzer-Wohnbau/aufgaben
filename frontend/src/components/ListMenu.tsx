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
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Text,
} from "@fluentui/react-components";
import { DeleteRegular, MoreHorizontalRegular, RenameRegular } from "@fluentui/react-icons";
import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { useApp } from "../context";
import { navigate } from "../route";
import type { TaskList } from "../types";

/** Menü einer eigenen Liste: umbenennen und löschen. Aufgaben bleiben beim Löschen erhalten. */
export function ListMenu({ list }: { list: TaskList }) {
  const queryClient = useQueryClient();
  const { me, notify } = useApp();
  const targetView = me.showInbox ? "aufgaben" : "geplant";
  const targetLabel = me.showInbox ? "Aufgaben" : "Geplant";
  const [mode, setMode] = useState<"rename" | "delete" | null>(null);
  const [name, setName] = useState(list.name);
  const [busy, setBusy] = useState(false);

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["lists"] }),
      queryClient.invalidateQueries({ queryKey: ["tasks"] }),
      queryClient.invalidateQueries({ queryKey: ["counts"] }),
    ]);

  const rename = async () => {
    const trimmed = name.trim();
    if (!trimmed || busy) return;
    setBusy(true);
    try {
      await api.updateList(list.id, { name: trimmed });
      await refresh();
      setMode(null);
    } catch (e) {
      notify(e instanceof Error ? e.message : "Umbenennen fehlgeschlagen", "error");
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (busy) return;
    setBusy(true);
    try {
      await api.deleteList(list.id);
      navigate({ page: "tasks", view: targetView });
      await refresh();
      notify(`Liste „${list.name}“ gelöscht`, "success");
    } catch (e) {
      notify(e instanceof Error ? e.message : "Löschen fehlgeschlagen", "error");
      setBusy(false);
    }
  };

  return (
    <>
      <Menu>
        <MenuTrigger disableButtonEnhancement>
          <Button appearance="subtle" icon={<MoreHorizontalRegular />} aria-label="Weitere Aktionen für die Liste" />
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            <MenuItem icon={<RenameRegular />} onClick={() => { setName(list.name); setMode("rename"); }}>
              Umbenennen
            </MenuItem>
            <MenuItem icon={<DeleteRegular />} onClick={() => setMode("delete")}>
              Liste löschen
            </MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>

      <Dialog open={mode === "rename"} onOpenChange={(_, d) => !d.open && setMode(null)}>
        <DialogSurface>
          <form onSubmit={(e) => { e.preventDefault(); void rename(); }}>
            <DialogBody>
              <DialogTitle>Liste umbenennen</DialogTitle>
              <DialogContent>
                <Field label="Name der Liste">
                  <Input value={name} maxLength={120} autoFocus onChange={(_, d) => setName(d.value)} />
                </Field>
              </DialogContent>
              <DialogActions>
                <Button onClick={() => setMode(null)}>Abbrechen</Button>
                <Button appearance="primary" type="submit" disabled={!name.trim() || busy}>
                  Speichern
                </Button>
              </DialogActions>
            </DialogBody>
          </form>
        </DialogSurface>
      </Dialog>

      <Dialog open={mode === "delete"} onOpenChange={(_, d) => !d.open && setMode(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Liste „{list.name}“ löschen?</DialogTitle>
            <DialogContent>
              <Text>
                Die Aufgaben der Liste bleiben erhalten. Eigene offene Aufgaben findest du danach unter „{targetLabel}“.
                {list.members.length > 0 ? " Die Liste wird für alle Mitglieder entfernt." : ""}
              </Text>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setMode(null)}>Abbrechen</Button>
              <Button appearance="primary" icon={<DeleteRegular />} disabled={busy} onClick={() => void remove()}>
                Löschen
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
}
