import { useState, type ReactElement, type SyntheticEvent } from "react";
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
  MenuDivider,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
} from "@fluentui/react-components";
import { CalendarLtrRegular } from "@fluentui/react-icons";
import { useApp } from "../context";
import { addDays, laterChoices, weekdayDate, type DateChoice } from "../later";

interface Props {
  /** Knopf, der das Menü öffnet */
  trigger: ReactElement;
  /** Vorgeschlagene Tage, Standard: morgen, nächste Woche, in einer oder zwei Wochen */
  choices?: DateChoice[];
  /** Überschrift beim eigenen Datum */
  title?: string;
  onPick: (date: string) => void;
}

/** Tag auswählen: Vorschläge mit Wochentag oder ein eigenes Datum ab morgen. */
export function LaterMenu({ trigger, choices, title = "Bis wann zurückstellen?", onPick }: Props) {
  const { today } = useApp();
  const [custom, setCustom] = useState<string | null>(null);
  const items = choices ?? laterChoices(today);
  const min = addDays(today, 1);

  // Menü und Dialog liegen im Portal, ihre Ereignisse laufen in React trotzdem zur Aufgabenzeile hoch:
  // dort würden sie die Aufgabe öffnen.
  const stop = (e: SyntheticEvent) => e.stopPropagation();

  return (
    <span onClick={stop} onKeyDown={stop} style={{ display: "contents" }}>
      <Menu>
        <MenuTrigger disableButtonEnhancement>{trigger}</MenuTrigger>
        <MenuPopover>
          <MenuList>
            {items.map((c) => (
              <MenuItem key={c.date} secondaryContent={weekdayDate(c.date)} onClick={() => onPick(c.date)}>
                {c.label}
              </MenuItem>
            ))}
            <MenuDivider />
            <MenuItem icon={<CalendarLtrRegular />} onClick={() => setCustom(min)}>
              Datum wählen …
            </MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>
      <Dialog open={custom !== null} onOpenChange={(_, d) => !d.open && setCustom(null)}>
        <DialogSurface style={{ maxWidth: 360 }}>
          <DialogBody>
            <DialogTitle>{title}</DialogTitle>
            <DialogContent>
              <Field label="Datum">
                <Input type="date" min={min} value={custom ?? ""} onChange={(_, d) => setCustom(d.value)} />
              </Field>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setCustom(null)}>Abbrechen</Button>
              <Button
                appearance="primary"
                disabled={!custom || custom < min}
                onClick={() => {
                  const value = custom!;
                  setCustom(null);
                  onPick(value);
                }}
              >
                Übernehmen
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </span>
  );
}
