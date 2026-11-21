import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  makeStyles,
  Text,
  tokens,
} from "@fluentui/react-components";

export interface ConfirmRequest {
  title: string;
  text?: string;
  /** Beschriftung der bestätigenden Schaltfläche, z. B. „In den Papierkorb“. */
  confirmLabel: string;
  /** Rot hervorheben – für Schritte, die sich nicht rückgängig machen lassen. */
  danger?: boolean;
}

const useStyles = makeStyles({
  danger: {
    backgroundColor: tokens.colorStatusDangerBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ":hover": { backgroundColor: tokens.colorStatusDangerForeground1, color: tokens.colorNeutralForegroundOnBrand },
    ":hover:active": { backgroundColor: tokens.colorStatusDangerForeground2, color: tokens.colorNeutralForegroundOnBrand },
  },
});

/**
 * Rückfrage vor dem Löschen u. ä. Ersetzt window.confirm: Das blockiert Teams in Tabs ohne Meldung
 * und liefert sofort „Abbrechen“ – die Aktion passierte dann einfach nicht.
 */
export function ConfirmDialog({ request, open, onAnswer }: { request: ConfirmRequest | null; open: boolean; onAnswer: (ok: boolean) => void }) {
  const styles = useStyles();
  return (
    <Dialog open={open && !!request} onOpenChange={(_, d) => !d.open && onAnswer(false)}>
      <DialogSurface style={{ maxWidth: 460 }}>
        <DialogBody>
          <DialogTitle>{request?.title}</DialogTitle>
          {request?.text && (
            <DialogContent>
              <Text>{request.text}</Text>
            </DialogContent>
          )}
          <DialogActions>
            <Button onClick={() => onAnswer(false)}>Abbrechen</Button>
            <Button appearance="primary" className={request?.danger ? styles.danger : undefined} onClick={() => onAnswer(true)}>
              {request?.confirmLabel}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
