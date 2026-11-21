import { Avatar, Button, Card, Caption1, makeStyles, Spinner, Subtitle1, Text, Title2, tokens } from "@fluentui/react-components";
import { CheckmarkCircleRegular } from "@fluentui/react-icons";
import { useQuery } from "@tanstack/react-query";
import { api } from "../api";
import { initials } from "../context";

const useStyles = makeStyles({
  page: {
    minHeight: "100%",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: "24px",
    boxSizing: "border-box",
    backgroundColor: tokens.colorNeutralBackground3,
  },
  card: { width: "min(440px, 100%)", padding: "32px", display: "flex", flexDirection: "column", gap: "16px" },
  logo: { fontSize: "48px", color: tokens.colorBrandForeground1 },
  devList: { display: "flex", flexDirection: "column", gap: "8px", marginTop: "8px" },
  devButton: { justifyContent: "flex-start", gap: "12px", height: "auto", padding: "10px 12px" },
  devText: { display: "flex", flexDirection: "column", alignItems: "flex-start" },
  hint: {
    padding: "8px 12px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorPaletteYellowBackground2,
    color: tokens.colorPaletteYellowForeground2,
  },
});

export function LoginScreen() {
  const styles = useStyles();
  const config = useQuery({ queryKey: ["auth-config"], queryFn: api.authConfig });
  const returnUrl = encodeURIComponent(window.location.pathname + window.location.search + window.location.hash);

  return (
    <div className={styles.page}>
      <Card className={styles.card}>
        <CheckmarkCircleRegular className={styles.logo} />
        <Title2>Aufgaben</Title2>
        <Text>Deine Aufgaben, Delegationen und Projektaufgaben an einem Ort, im Browser und in Teams.</Text>
        {config.isLoading && <Spinner size="tiny" />}
        {config.data?.mode === "entra" && (
          <Button appearance="primary" size="large" as="a" href={`/auth/login?returnUrl=${returnUrl}`}>
            Mit Firmenkonto anmelden
          </Button>
        )}
        {config.data?.mode === "dev" && (
          <>
            <Caption1 className={styles.hint}>Entwicklungsmodus: Anmeldung ohne Entra, nur auf diesem Rechner.</Caption1>
            <Subtitle1>Als Testperson anmelden</Subtitle1>
            <div className={styles.devList}>
              {config.data.devUsers?.map((u) => (
                <Button
                  key={u.oid}
                  className={styles.devButton}
                  appearance="outline"
                  as="a"
                  href={`/auth/dev-login?oid=${encodeURIComponent(u.oid)}&returnUrl=${returnUrl}`}
                >
                  <Avatar name={u.name} initials={initials(u.name)} color="colorful" />
                  <span className={styles.devText}>
                    <Text weight="semibold">{u.name}</Text>
                    <Caption1>
                      {u.jobTitle}
                      {u.isAdmin ? " · Verwaltung" : ""}
                    </Caption1>
                  </span>
                </Button>
              ))}
            </div>
          </>
        )}
      </Card>
    </div>
  );
}
