import { Button, makeStyles, Text, tokens } from "@fluentui/react-components";
import { LightbulbRegular, WeatherMoonRegular, WeatherSunnyRegular } from "@fluentui/react-icons";
import { longDate } from "../dates";

const useStyles = makeStyles({
  hero: {
    position: "relative",
    // "clip" statt "hidden": Der Lichtkreis ragt über den Rand, mit "hidden" wäre der Bereich
    // scrollbar, und der Browser verschiebt ihn beim Fokussieren eines Knopfs.
    overflow: "clip",
    margin: "16px 32px 4px",
    padding: "22px 24px",
    borderRadius: "16px",
    color: tokens.colorNeutralForegroundOnBrand,
    backgroundImage: `linear-gradient(125deg, ${tokens.colorBrandBackground} 0%, ${tokens.colorBrandBackgroundPressed} 55%, #8b5cf6 100%)`,
    boxShadow: tokens.shadow8,
    display: "flex",
    alignItems: "center",
    gap: "20px",
    animationName: "aufgaben-fade-in",
    animationDuration: "300ms",
    // Handy: Text und Knopf untereinander, der Ring rechts daneben; oben Platz für den Menüknopf.
    "@media (max-width: 760px)": {
      margin: "56px 16px 4px",
      padding: "18px 18px 12px",
      display: "grid",
      gridTemplateColumns: "minmax(0, 1fr) auto",
      columnGap: "12px",
      rowGap: "4px",
    },
  },
  compact: { margin: "12px 16px 4px", padding: "16px" },
  inTour: { "@media (max-width: 760px)": { marginTop: "12px" } },
  glow: {
    position: "absolute",
    right: "-60px",
    top: "-80px",
    width: "260px",
    height: "260px",
    borderRadius: "50%",
    backgroundColor: "rgba(255,255,255,0.12)",
    pointerEvents: "none",
  },
  text: {
    flex: 1,
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    zIndex: 1,
    "@media (max-width: 760px)": { gridColumn: "1", gridRow: "1" },
  },
  greeting: {
    fontSize: "26px",
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: "32px",
    "@media (max-width: 760px)": { fontSize: "22px", lineHeight: "28px" },
  },
  sub: { opacity: 0.9 },
  ring: {
    position: "relative",
    width: "72px",
    height: "72px",
    flexShrink: 0,
    zIndex: 1,
    "@media (max-width: 760px)": { gridColumn: "2", gridRow: "1 / span 2", alignSelf: "center" },
  },
  ringText: {
    position: "absolute",
    inset: 0,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: "16px",
  },
  button: {
    zIndex: 1,
    color: tokens.colorNeutralForegroundOnBrand,
    ":hover": { color: tokens.colorNeutralForegroundOnBrand },
    "@media (max-width: 760px)": { gridColumn: "1", gridRow: "2", justifySelf: "start", marginLeft: "-10px" },
  },
});

function greeting(hour: number) {
  if (hour < 11) return "Guten Morgen";
  if (hour < 18) return "Hallo";
  return "Guten Abend";
}

interface Props {
  name: string;
  today: string;
  done: number;
  total: number;
  compact: boolean;
  tourActive?: boolean;
  suggestionsOpen: boolean;
  onToggleSuggestions: () => void;
}

/** Kopf von "Mein Tag": Begrüßung, Datum, Fortschrittsring und Vorschläge. */
export function MyDayHero({ name, today, done, total, compact, tourActive, suggestionsOpen, onToggleSuggestions }: Props) {
  const styles = useStyles();
  const hour = new Date().getHours();
  const share = total === 0 ? 0 : done / total;
  const r = 30;
  const circumference = 2 * Math.PI * r;
  const firstName = name.split(" ")[0];
  const message =
    total === 0 ? "Noch nichts geplant, hol dir Vorschläge." : done === total ? "Alles erledigt. Stark!" : `${total - done} von ${total} noch offen`;

  return (
    <section className={`${styles.hero} ${compact ? styles.compact : ""} ${tourActive ? styles.inTour : ""}`} aria-label="Mein Tag">
      <span className={styles.glow} />
      <div className={styles.text}>
        <span className={styles.greeting}>
          {hour >= 18 ? <WeatherMoonRegular /> : <WeatherSunnyRegular />} {greeting(hour)}, {firstName}
        </span>
        <Text className={styles.sub}>
          {longDate(today)} · {message}
        </Text>
      </div>
      <Button
        className={styles.button}
        appearance={suggestionsOpen ? "primary" : "transparent"}
        icon={<LightbulbRegular />}
        onClick={onToggleSuggestions}
      >
        Vorschläge
      </Button>
      <div className={styles.ring} role="img" aria-label={`${done} von ${total} erledigt`}>
        <svg width="72" height="72" viewBox="0 0 72 72">
          <circle cx="36" cy="36" r={r} fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="7" />
          <circle
            cx="36"
            cy="36"
            r={r}
            fill="none"
            stroke="#ffffff"
            strokeWidth="7"
            strokeLinecap="round"
            strokeDasharray={circumference}
            strokeDashoffset={circumference * (1 - share)}
            transform="rotate(-90 36 36)"
            style={{ transition: "stroke-dashoffset 600ms cubic-bezier(0.22, 1, 0.36, 1)" }}
          />
        </svg>
        <span className={styles.ringText}>
          <span>{Math.round(share * 100)}%</span>
        </span>
      </div>
    </section>
  );
}
