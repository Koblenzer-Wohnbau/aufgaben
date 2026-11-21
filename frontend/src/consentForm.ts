import type { AccessGrant } from "./types";

function esc(value: string): string {
  return value.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
}

function de(date: string | null): string {
  if (!date) return "";
  const [y, m, d] = date.split("-");
  return `${d}.${m}.${y}`;
}

/**
 * Druckbares Formular (A4) zur Einsicht in Aufgaben. Wird über einen unsichtbaren Rahmen gedruckt
 * oder als HTML-Datei heruntergeladen; alle Werte werden HTML-maskiert.
 */
export function consentFormHtml(g: AccessGrant): string {
  const box = (checked: boolean) => (checked ? "☒" : "☐");
  const until = g.validUntil ? `bis ${de(g.validUntil)}` : "auf unbestimmte Zeit (bis zum Widerruf)";
  const created = new Date(g.createdAt).toLocaleDateString("de-DE");
  const sign = (role: string) => `
    <div class="sign">
      <div class="line"></div>
      <div class="cap"><span>Ort, Datum</span><span>${esc(role)}</span></div>
    </div>`;

  return `<!doctype html>
<html lang="de"><head><meta charset="utf-8"><title>Einverständniserklärung Einsicht in Aufgaben – Vorgang ${g.id}</title>
<style>
  @page { size: A4; margin: 18mm 18mm 16mm; }
  * { box-sizing: border-box; }
  body { font-family: "Segoe UI", Arial, sans-serif; color: #111; font-size: 10.5pt; line-height: 1.45; margin: 0; }
  h1 { font-size: 16pt; margin: 0 0 2mm; font-weight: 600; }
  h2 { font-size: 11.5pt; margin: 6mm 0 2mm; font-weight: 600; }
  .sub { color: #555; font-size: 9pt; margin-bottom: 5mm; }
  table { width: 100%; border-collapse: collapse; margin: 2mm 0; }
  th, td { text-align: left; vertical-align: top; padding: 2.2mm 2mm; border-bottom: 0.3mm solid #ccc; }
  th { width: 42mm; color: #444; font-weight: 600; }
  ul { margin: 1mm 0 0 5mm; padding: 0; }
  li { margin-bottom: 1mm; }
  .blank { display: inline-block; min-width: 70mm; border-bottom: 0.3mm solid #444; height: 4.5mm; }
  .signs { margin-top: 8mm; display: grid; grid-template-columns: 1fr 1fr; gap: 10mm 12mm; }
  .sign .line { border-bottom: 0.3mm solid #111; height: 14mm; }
  .sign .cap { display: flex; justify-content: space-between; font-size: 8.5pt; color: #444; margin-top: 1mm; }
  .foot { margin-top: 8mm; font-size: 8pt; color: #666; border-top: 0.3mm solid #ccc; padding-top: 2mm; }
  .screen { background: #eef0fb; border-radius: 2mm; padding: 2.5mm 3.5mm; margin-bottom: 5mm; font-size: 9.5pt; }
  @media screen { body { max-width: 190mm; margin: 10mm auto; padding: 0 5mm; } }
  @media print { .screen { display: none; } }
</style></head>
<body>
  <div class="screen">Zum Drucken <strong>Strg + P</strong> drücken. Dieser Hinweis erscheint nicht auf dem Ausdruck.</div>
  <h1>Einverständniserklärung zur Einsicht in Aufgaben</h1>
  <div class="sub">App „Aufgaben“ · Vorgang Nr. ${g.id} · erstellt am ${esc(created)}</div>

  <table>
    <tr><th>Organisation</th><td><span class="blank"></span></td></tr>
    <tr><th>Betroffene Person<br><small>(deren Aufgaben eingesehen werden)</small></th><td>${esc(g.owner.name)}${g.owner.email ? `<br>${esc(g.owner.email)}` : ""}</td></tr>
    <tr><th>Einsicht erhält</th><td>${esc(g.viewer.name)}${g.viewer.email ? `<br>${esc(g.viewer.email)}` : ""}</td></tr>
    <tr><th>Umfang</th><td>${box(g.permission === "lesen")} nur lesen &nbsp;&nbsp; ${box(g.permission === "bearbeiten")} lesen und bearbeiten</td></tr>
    <tr><th>Zeitraum</th><td>ab ${esc(de(g.validFrom))} ${esc(until)}</td></tr>
    <tr><th>Zweck</th><td>${esc(g.purpose)}</td></tr>
  </table>

  <h2>Erklärung der betroffenen Person</h2>
  <p>Ich bin damit einverstanden, dass die oben genannte Person im angegebenen Umfang und Zeitraum Einsicht in meine
  Aufgaben in der App „Aufgaben“ erhält. Das umfasst auch Aufgaben, die sonst nur für mich sichtbar sind
  (persönliche Aufgaben mit Notizen, Checklisten, Kommentaren und Anhängen).</p>
  <ul>
    <li>Die Einwilligung ist freiwillig. Aus einer Verweigerung oder einem Widerruf entstehen mir keine Nachteile.</li>
    <li>Ich kann die Einwilligung jederzeit ohne Angabe von Gründen mit Wirkung für die Zukunft widerrufen – formlos bei der Verwaltung.</li>
    <li>Jeder Zugriff über diese Freigabe wird protokolliert. Das Protokoll kann ich in der App unter „Einstellungen“ jederzeit einsehen.</li>
    <li>Die Freigabe endet automatisch zum angegebenen Enddatum.</li>
  </ul>

  <h2>Verpflichtung der einsehenden Person</h2>
  <p>Ich nutze die Einsicht ausschließlich für den oben genannten Zweck, gebe keine Inhalte unbefugt weiter
  und verändere Aufgaben nur, soweit der Umfang „lesen und bearbeiten“ vereinbart ist.</p>

  <div class="signs">
    ${sign("Unterschrift betroffene Person")}
    ${sign("Unterschrift einsehende Person")}
    ${sign("Unterschrift Verwaltung (Freigabe erteilt)")}
    ${sign("Zur Kenntnis (z. B. Betriebsrat, optional)")}
  </div>

  <div class="foot">Nach der Unterschrift: in der App unter „Verwaltung → Einsichtsrechte“ die Unterschrift bestätigen und den Ablageort des Originals eintragen.
  Erst danach wird die Einsicht wirksam.</div>
</body></html>`;
}

const PRINT_FRAME_ID = "aufgaben-formular-druck";

/**
 * Druckt das Formular über einen unsichtbaren Rahmen in der Seite. Ein neues Fenster blockieren
 * Browser und Teams als Pop-up – vor allem, wenn der Druck erst nach dem Anlegen startet.
 * Der Rahmen bekommt das Formular ohne eigenes Skript; den Druck startet die App.
 * Gibt false zurück, wenn der Druck nicht gestartet werden konnte.
 */
export function printConsentForm(grant: AccessGrant): boolean {
  try {
    document.getElementById(PRINT_FRAME_ID)?.remove();
    const frame = document.createElement("iframe");
    frame.id = PRINT_FRAME_ID;
    frame.title = "Formular zum Drucken";
    frame.setAttribute("aria-hidden", "true");
    frame.tabIndex = -1;
    Object.assign(frame.style, { position: "absolute", width: "0", height: "0", top: "-10px", left: "-10px", border: "0" });
    document.body.appendChild(frame);
    const doc = frame.contentDocument;
    const win = frame.contentWindow;
    if (!doc || !win) return false;
    doc.open();
    doc.write(consentFormHtml(grant));
    doc.close();
    setTimeout(() => {
      try {
        win.focus();
        win.print();
      } catch {
        /* Druck abgelehnt – „Herunterladen“ bleibt als Weg */
      }
    }, 300);
    return true;
  } catch {
    return false;
  }
}

/** Dateiname für das heruntergeladene Formular, ohne Zeichen, die Windows in Dateinamen nicht erlaubt. */
export function consentFormFileName(g: Pick<AccessGrant, "id" | "owner">): string {
  const owner = g.owner.name.replace(/[<>:"/\\|?*\u0000-\u001f]/g, "").replace(/\s+/g, " ").trim();
  return `Einsicht in Aufgaben - Nr. ${g.id}${owner ? ` - ${owner}` : ""}.html`;
}

/** Formular als HTML-Datei herunterladen – öffnet im Browser und lässt sich dort mit Strg+P drucken. */
export function downloadConsentForm(grant: AccessGrant) {
  const url = URL.createObjectURL(new Blob([consentFormHtml(grant)], { type: "text/html;charset=utf-8" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = consentFormFileName(grant);
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
