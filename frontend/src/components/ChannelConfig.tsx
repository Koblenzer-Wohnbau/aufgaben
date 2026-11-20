import { useEffect, useState } from "react";
import { Dropdown, Field, makeStyles, Option, Radio, RadioGroup, Text, Title3, tokens } from "@fluentui/react-components";
import { api } from "../api";
import { useApp } from "../context";
import { registerChannelTabSave, setChannelTabValid, type TeamsInfo } from "../teams";

const useStyles = makeStyles({
  page: { padding: "24px", display: "flex", flexDirection: "column", gap: "16px", maxWidth: "560px" },
  muted: { color: tokens.colorNeutralForeground3 },
});

/**
 * Konfigurationsseite, wenn jemand den Tab "Aufgaben" zu einem Teams-Kanal hinzufügt.
 * Standard: Kanal wird automatisch als Projekt angelegt ("1001 Lindenpark" → Nummer 1001).
 */
export function ChannelConfig({ teams }: { teams: TeamsInfo | null }) {
  const styles = useStyles();
  const { projects } = useApp();
  const [mode, setMode] = useState<"neu" | "vorhanden">("neu");
  const [projectId, setProjectId] = useState<number | null>(null);

  const valid = mode === "neu" || projectId !== null;

  useEffect(() => {
    if (!teams) return;
    setChannelTabValid(valid);
    registerChannelTabSave(async () => {
      const project = await api.linkChannel({
        channelId: teams.channelId ?? "",
        teamId: teams.teamId,
        teamName: teams.teamName,
        channelName: teams.channelName,
        projectId: mode === "vorhanden" ? projectId ?? undefined : undefined,
      });
      return { projectId: project.id, label: project.number ?? project.name };
    });
  }, [teams, mode, projectId, valid]);

  if (!teams?.channelId) {
    return (
      <div className={styles.page}>
        <Text>Diese Seite wird nur beim Hinzufügen des Tabs in einem Teams-Kanal verwendet.</Text>
      </div>
    );
  }

  return (
    <div className={styles.page}>
      <Title3>Aufgaben für „{teams.channelName}“</Title3>
      <Text className={styles.muted}>
        Der Tab zeigt alle Aufgaben des Projekts – für alle im Team. Neue Aufgaben im Tab gehören automatisch zum Projekt.
      </Text>
      <RadioGroup value={mode} onChange={(_, d) => setMode(d.value as "neu" | "vorhanden")}>
        <Radio value="neu" label={`Kanal als Projekt verwenden („${teams.channelName}“)`} />
        <Radio value="vorhanden" label="Mit einem vorhandenen Projekt verknüpfen" />
      </RadioGroup>
      {mode === "vorhanden" && (
        <Field label="Projekt">
          <Dropdown placeholder="Projekt wählen" onOptionSelect={(_, d) => setProjectId(d.optionValue ? Number(d.optionValue) : null)}>
            {projects.map((p) => (
              <Option key={p.id} value={String(p.id)} text={p.label}>
                {p.label}
              </Option>
            ))}
          </Dropdown>
        </Field>
      )}
    </div>
  );
}
