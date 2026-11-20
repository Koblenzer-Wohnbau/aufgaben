import { useState } from "react";
import {
  Badge,
  Button,
  Caption1,
  Card,
  CardHeader,
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
  Option,
  Spinner,
  Text,
  Title2,
  tokens,
} from "@fluentui/react-components";
import { DocumentBulletListRegular, FlashRegular } from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";
import { useApp } from "../context";
import { navigate } from "../route";
import type { Template, UserRef } from "../types";
import { PersonPicker } from "./PersonPicker";

const useStyles = makeStyles({
  view: { flex: 1, overflowY: "auto", padding: "20px 32px 48px" },
  header: { display: "flex", alignItems: "center", gap: "12px", marginBottom: "4px" },
  icon: { fontSize: "28px", color: tokens.colorBrandForeground1 },
  muted: { color: tokens.colorNeutralForeground3 },
  grid: { display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))", gap: "16px", marginTop: "20px" },
  steps: { margin: "4px 0 0", paddingLeft: "20px", display: "flex", flexDirection: "column", gap: "4px" },
  stepRow: { display: "grid", gridTemplateColumns: "1fr 200px", gap: "8px", alignItems: "center" },
  form: { display: "flex", flexDirection: "column", gap: "14px" },
});

function offsetLabel(days: number) {
  if (days === 0) return "am Stichtag";
  return days > 0 ? `+${days} Tage` : `${days} Tage`;
}

export function TemplatesView() {
  const styles = useStyles();
  const templates = useQuery({ queryKey: ["templates"], queryFn: api.templates });
  const [selected, setSelected] = useState<Template | null>(null);

  return (
    <div className={styles.view}>
      <div className={styles.header}>
        <DocumentBulletListRegular className={styles.icon} />
        <Title2 as="h1">Vorlagen</Title2>
      </div>
      <Text className={styles.muted}>
        Wiederkehrende Abläufe mit einem Klick starten: Alle Schritte werden als Aufgaben angelegt – mit Termin relativ zum Stichtag, Checkliste und zuständiger Person.
      </Text>
      {templates.isLoading && <Spinner style={{ marginTop: 32 }} />}
      <div className={styles.grid}>
        {templates.data?.map((t) => (
          <Card key={t.id}>
            <CardHeader
              header={<Text weight="semibold" size={400}>{t.name}</Text>}
              description={<Caption1 className={styles.muted}>{t.description}</Caption1>}
              action={
                <Button appearance="primary" icon={<FlashRegular />} onClick={() => setSelected(t)}>
                  Starten
                </Button>
              }
            />
            <ol className={styles.steps}>
              {t.steps.map((s) => (
                <li key={s.id}>
                  <Text size={300}>{s.title}</Text>{" "}
                  <Badge appearance="tint" size="small" color={s.offsetDays < 0 ? "informative" : "brand"}>
                    {offsetLabel(s.offsetDays)}
                  </Badge>
                  {s.roleHint && (
                    <Caption1 className={styles.muted}> · {s.roleHint}</Caption1>
                  )}
                </li>
              ))}
            </ol>
          </Card>
        ))}
      </div>
      {selected && <ApplyDialog template={selected} onClose={() => setSelected(null)} />}
    </div>
  );
}

function ApplyDialog({ template, onClose }: { template: Template; onClose: () => void }) {
  const styles = useStyles();
  const { today, projects, users, me, notify } = useApp();
  const queryClient = useQueryClient();
  const [startDate, setStartDate] = useState(today);
  const [projectId, setProjectId] = useState<number | null>(null);
  const [suffix, setSuffix] = useState("");
  const [defaultPerson, setDefaultPerson] = useState<UserRef | null>(users.find((u) => u.id === me.id) ?? null);
  const [perStep, setPerStep] = useState<Record<number, UserRef | null>>({});
  const [busy, setBusy] = useState(false);

  const apply = async () => {
    setBusy(true);
    try {
      const assignees: Record<number, number> = {};
      for (const [stepId, user] of Object.entries(perStep)) if (user) assignees[Number(stepId)] = user.id;
      const result = await api.applyTemplate(template.id, {
        startDate,
        projectId,
        defaultAssigneeId: defaultPerson?.id ?? null,
        assignees,
        titleSuffix: suffix || undefined,
      });
      await queryClient.invalidateQueries();
      notify(`${result.created} Aufgaben angelegt`);
      onClose();
      const people = [defaultPerson, ...Object.values(perStep)].filter((u): u is NonNullable<typeof u> => !!u);
      if (projectId) navigate({ page: "tasks", view: "projekt", id: projectId });
      else if (people.some((u) => u.id !== me.id)) navigate({ page: "tasks", view: "delegiert" });
      else navigate({ page: "tasks", view: "aufgaben" });
    } catch (e) {
      notify(e instanceof Error ? e.message : "Fehler", "error");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface style={{ maxWidth: 680 }}>
        <DialogBody>
          <DialogTitle>{template.name} starten</DialogTitle>
          <DialogContent className={styles.form}>
            <Field label="Stichtag" required hint={template.description}>
              <Input type="date" value={startDate} onChange={(_, d) => setStartDate(d.value)} />
            </Field>
            <Field label="Zusatz für alle Titel" hint="z. B. Wohnung, Adresse oder Mietpartei">
              <Input value={suffix} placeholder="Whg. 3.2 Gartenstraße 12" onChange={(_, d) => setSuffix(d.value)} />
            </Field>
            <Field label="Projekt (optional)">
              <Dropdown placeholder="Kein Projekt" onOptionSelect={(_, d) => setProjectId(d.optionValue ? Number(d.optionValue) : null)}>
                <Option value="">Kein Projekt</Option>
                {projects.map((p) => (
                  <Option key={p.id} value={String(p.id)} text={p.label}>
                    {p.label}
                  </Option>
                ))}
              </Dropdown>
            </Field>
            <Field label="Standardmäßig zuständig">
              <PersonPicker users={users} value={defaultPerson} onChange={setDefaultPerson} />
            </Field>
            <Text weight="semibold">Zuständig je Schritt (optional)</Text>
            {template.steps.map((s) => (
              <div key={s.id} className={styles.stepRow}>
                <Text size={300}>
                  {s.title}
                  {s.roleHint && <Caption1 className={styles.muted}> · {s.roleHint}</Caption1>}
                </Text>
                <PersonPicker
                  users={users}
                  value={perStep[s.id] ?? null}
                  placeholder={defaultPerson?.name ?? "Person"}
                  onChange={(u) => setPerStep((p) => ({ ...p, [s.id]: u }))}
                />
              </div>
            ))}
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Abbrechen</Button>
            <Button appearance="primary" disabled={!startDate || busy} onClick={apply}>
              {busy ? "Lege an …" : `${template.steps.length} Aufgaben anlegen`}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
