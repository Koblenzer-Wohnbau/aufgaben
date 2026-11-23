import { describe, expect, it } from "vitest";
import { applyAnswer, clipboardInput, createdMessage, currentOption, dataUrlBytes, draftNotes, fitSize, modelLabel, plainTitle, teamsDraftText, thinkingText } from "./ai";
import type { AiQuestion, AiTaskDraft, UserRef } from "./types";

const draft: AiTaskDraft = {
  title: "Angebot Dachdecker prüfen",
  notes: "Angebot liegt als PDF vor.",
  dueDate: "2026-11-27",
  dueTime: null,
  priority: "normal",
  kind: "aufgabe",
  assignee: null,
  project: null,
  checklist: [],
  source: "bis Freitag das Angebot prüfen",
  questions: [],
};
const users: UserRef[] = [
  { id: 2, name: "Thomas Becker", email: null, jobTitle: "Bauleitung" },
  { id: 5, name: "Thomas Schmitt", email: null, jobTitle: "Technik" },
];

describe("KI-Eingabe", () => {
  it("verkleinert nur, nie größer, und behält das Seitenverhältnis", () => {
    expect(fitSize(3600, 1800, 1800)).toEqual({ width: 1800, height: 900 });
    expect(fitSize(800, 600, 1800)).toEqual({ width: 800, height: 600 });
    expect(fitSize(1000, 4000, 1000)).toEqual({ width: 250, height: 1000 });
  });

  it("rechnet die Größe hinter einer Data-URL", () => {
    expect(dataUrlBytes("data:image/jpeg;base64,QUJD")).toBe(3);
    expect(dataUrlBytes("data:image/jpeg;base64,QUI=")).toBe(2);
    expect(dataUrlBytes("data:image/jpeg;base64,QQ==")).toBe(1);
  });

  it("nimmt aus der Zwischenablage Text und Bild", () => {
    const image = new File([new Uint8Array([1])], "image.png", { type: "image/png" });
    const fake = (text: string, files: File[]) =>
      ({ getData: (t: string) => (t === "text/plain" ? text : ""), files, items: [] }) as unknown as DataTransfer;
    expect(clipboardInput(fake("  Bitte bis Freitag …  ", []))).toEqual({ text: "Bitte bis Freitag …" });
    expect(clipboardInput(fake("", [image]))).toEqual({ image });
    expect(clipboardInput(fake("", []))).toBeNull();
  });

  it("zeigt GLM 5.3 statt der Modellkennung", () => {
    expect(modelLabel("zai-glm-latest")).toBe("GLM 5.3");
    expect(modelLabel("mistral-large-latest")).toBe("mistral-large-latest");
  });

  it("erklärt das Warten", () => {
    expect(thinkingText(1)).toBe("Die KI liest den Text …");
    expect(thinkingText(12)).toBe("Die KI denkt nach … 12 s");
  });

  it("meldet, wer benachrichtigt wurde", () => {
    expect(createdMessage(1, [])).toBe("Aufgabe angelegt");
    expect(createdMessage(3, ["Thomas Becker"])).toBe("3 Aufgaben angelegt, Thomas Becker wurde benachrichtigt");
    expect(createdMessage(4, ["Anna", "Thomas", "Maria"])).toBe("4 Aufgaben angelegt, Anna, Thomas und Maria wurden benachrichtigt");
  });

  it("übernimmt Antworten auf Rückfragen ins Feld", () => {
    const who: AiQuestion = { field: "verantwortlich", text: "Welcher Thomas?", options: [{ value: "2", label: "Thomas Becker" }, { value: "5", label: "Thomas Schmitt" }] };
    const answered = applyAnswer(draft, who, who.options[1], users);
    expect(answered.assignee?.name).toBe("Thomas Schmitt");
    expect(currentOption(answered, who, 1)).toBe("5");
    expect(currentOption(draft, who, 1)).toBe("1"); // ohne Wahl: die eingebende Person

    const when: AiQuestion = { field: "faellig", text: "Welcher Freitag?", options: [{ value: "2026-11-27", label: "Fr 27.11." }, { value: "2026-12-04", label: "Fr 4.12." }] };
    expect(applyAnswer(draft, when, when.options[1], users).dueDate).toBe("2026-12-04");
  });

  it("hängt das Zitat an die Notizen", () => {
    expect(draftNotes(draft)).toBe("Angebot liegt als PDF vor.\n\nAus: „bis Freitag das Angebot prüfen“");
    expect(draftNotes({ ...draft, notes: null, source: null })).toBeUndefined();
    const link = "https://teams.microsoft.com/l/message/19:a/1";
    expect(draftNotes({ ...draft, notes: null, source: null }, link)).toBe(`Teams-Nachricht: ${link}`);
  });

  it("stellt einer Teams-Nachricht Absender und Zeit voran", () => {
    const at = () => "21.11.2026, 10:15";
    expect(teamsDraftText({ text: " Kannst du das Angebot prüfen? ", from: "Thomas Becker", sentAt: "2026-11-21T09:15:00Z", link: null }, at)).toBe(
      "Teams-Nachricht von Thomas Becker (21.11.2026, 10:15):\nKannst du das Angebot prüfen?",
    );
    expect(teamsDraftText({ text: "Nur Text", from: null, sentAt: null, link: null })).toBe("Nur Text");
    // Nachricht nur mit Bild: kein Kopf ohne Inhalt, die Person tippt selbst
    expect(teamsDraftText({ text: "", from: "Thomas Becker", sentAt: null, link: null })).toBe("");
  });

  it("nimmt ohne KI die erste Zeile nach der Anrede als Titel", () => {
    expect(plainTitle("Hallo Anna,\nkannst du das Angebot prüfen?")).toBe("kannst du das Angebot prüfen?");
    expect(plainTitle("Guten Morgen zusammen!\n\nZähler im Lindenpark ablesen")).toBe("Zähler im Lindenpark ablesen");
    expect(plainTitle("Hallo")).toBe("Hallo");
    expect(plainTitle("Hallo Anna, bitte bis Freitag den Mieterwechsel in der 1001 vorbereiten.")).toBe(
      "Hallo Anna, bitte bis Freitag den Mieterwechsel in der 1001 vorbereiten.",
    );
    expect(plainTitle("x".repeat(200))).toHaveLength(118);
  });
});
