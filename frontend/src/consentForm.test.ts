import { describe, expect, it } from "vitest";
import { consentFormFileName, consentFormHtml } from "./consentForm";
import type { AccessGrant } from "./types";

const grant: AccessGrant = {
  id: 42,
  owner: { id: 1, name: "Anna <script>alert(1)</script> Schneider", email: "anna@example.test", jobTitle: null },
  viewer: { id: 2, name: "Thomas Becker", email: null, jobTitle: null },
  permission: "lesen",
  validFrom: "2026-12-22",
  validUntil: "2027-01-06",
  purpose: 'Urlaubsvertretung "Haus 2" & Co.',
  status: "entwurf",
  isCurrentlyActive: false,
  createdAt: "2026-11-20T10:00:00Z",
  confirmedAt: null,
  consentNote: "",
  revokedAt: null,
  revokeReason: "",
  accessCount: 0,
};

describe("consentFormHtml", () => {
  it("maskiert alle Werte – kein eingeschleustes HTML im Formular", () => {
    const html = consentFormHtml(grant);
    expect(html).not.toContain("<script>alert(1)</script>");
    expect(html).toContain("Anna &lt;script&gt;alert(1)&lt;/script&gt; Schneider");
    expect(html).toContain("Urlaubsvertretung &quot;Haus 2&quot; &amp; Co.");
  });

  it("enthält Zeitraum, Umfang und Vorgangsnummer", () => {
    const html = consentFormHtml(grant);
    expect(html).toContain("ab 22.12.2026 bis 06.01.2027");
    expect(html).toContain("☒ nur lesen");
    expect(html).toContain("☐ lesen und bearbeiten");
    expect(html).toContain("Vorgang Nr. 42");
  });

  it("nennt unbefristete Freigaben ausdrücklich", () => {
    expect(consentFormHtml({ ...grant, validUntil: null })).toContain("auf unbestimmte Zeit (bis zum Widerruf)");
  });

  it("zeigt den Druckhinweis nur am Bildschirm", () => {
    const html = consentFormHtml(grant);
    expect(html).toContain("Strg + P");
    expect(html).toMatch(/@media print \{ \.screen \{ display: none; \} \}/);
  });

  it("benennt die heruntergeladene Datei ohne Zeichen, die Windows ablehnt", () => {
    expect(consentFormFileName({ id: 7, owner: { ...grant.owner, name: "Maria Klein" } })).toBe("Einsicht in Aufgaben - Nr. 7 - Maria Klein.html");
    expect(consentFormFileName({ id: 8, owner: { ...grant.owner, name: 'A/B: "C" <D>?' } })).toBe("Einsicht in Aufgaben - Nr. 8 - AB C D.html");
    expect(consentFormFileName({ id: 9, owner: { ...grant.owner, name: "  " } })).toBe("Einsicht in Aufgaben - Nr. 9.html");
  });
});
