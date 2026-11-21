import { describe, expect, it } from "vitest";
import { isPreviewable, MAX_UPLOAD_BYTES, uploadName, uploadProblem } from "./attachments";

describe("Anhänge", () => {
  const now = new Date(2026, 10, 21, 14, 5, 9);

  it("benennt eingefügte Screenshots mit Datum und Uhrzeit", () => {
    expect(uploadName({ name: "image.png", type: "image/png" }, now)).toBe("Bildschirmfoto 2026-11-21 14-05-09.png");
    expect(uploadName({ name: "", type: "image/jpeg" }, now)).toBe("Bildschirmfoto 2026-11-21 14-05-09.jpg");
  });

  it("behält echte Dateinamen", () => {
    expect(uploadName({ name: "Angebot Dach.pdf", type: "application/pdf" }, now)).toBe("Angebot Dach.pdf");
    expect(uploadName({ name: "Mängel Treppenhaus.png", type: "image/png" }, now)).toBe("Mängel Treppenhaus.png");
  });

  it("zeigt nur Bilder als Vorschau, kein SVG", () => {
    expect(isPreviewable("image/png")).toBe(true);
    expect(isPreviewable("IMAGE/JPEG")).toBe(true);
    expect(isPreviewable("image/svg+xml")).toBe(false);
    expect(isPreviewable("application/pdf")).toBe(false);
  });

  it("lehnt leere und zu große Dateien vorab ab", () => {
    expect(uploadProblem({ size: 0 })).toMatch(/leer/);
    expect(uploadProblem({ size: MAX_UPLOAD_BYTES + 1 })).toMatch(/10 MB/);
    expect(uploadProblem({ size: 1234 })).toBeNull();
  });
});
