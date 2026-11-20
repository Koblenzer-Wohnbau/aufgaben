import { makeStyles, Text, tokens } from "@fluentui/react-components";

const useStyles = makeStyles({
  empty: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: "12px",
    padding: "48px 16px",
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    animationName: "aufgaben-fade-in",
    animationDuration: "300ms",
  },
  art: { width: "160px", height: "120px" },
});

/** Freundliche leere Ansicht mit kleiner Illustration (Sonne für "Mein Tag", sonst Häkchen-Karte). */
export function EmptyState({ text, sunny }: { text: string; sunny?: boolean }) {
  const styles = useStyles();
  return (
    <div className={styles.empty}>
      {sunny ? (
        <svg className={styles.art} viewBox="0 0 160 120" aria-hidden="true">
          <circle cx="80" cy="62" r="34" fill="#ffd166" />
          <circle cx="80" cy="62" r="24" fill="#ffb703" />
          {Array.from({ length: 12 }, (_, i) => {
            const a = (i * Math.PI) / 6;
            return (
              <line
                key={i}
                x1={80 + Math.cos(a) * 42}
                y1={62 + Math.sin(a) * 42}
                x2={80 + Math.cos(a) * 54}
                y2={62 + Math.sin(a) * 54}
                stroke="#ffb703"
                strokeWidth="5"
                strokeLinecap="round"
              />
            );
          })}
          <path d="M68 62l8 8 16-17" fill="none" stroke="#ffffff" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      ) : (
        <svg className={styles.art} viewBox="0 0 160 120" aria-hidden="true">
          <rect x="34" y="18" width="92" height="84" rx="12" fill={tokens.colorBrandBackground2} />
          <rect x="48" y="36" width="64" height="8" rx="4" fill={tokens.colorBrandStroke2} />
          <rect x="48" y="54" width="48" height="8" rx="4" fill={tokens.colorBrandStroke2} />
          <rect x="48" y="72" width="56" height="8" rx="4" fill={tokens.colorBrandStroke2} />
          <circle cx="118" cy="92" r="20" fill="#5b5fc7" />
          <path d="M109 92l6 6 12-13" fill="none" stroke="#ffffff" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      )}
      <Text size={400}>{text}</Text>
    </div>
  );
}
