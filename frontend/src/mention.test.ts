import { describe, expect, it } from "vitest";
import { matchUser, mentionFor } from "./context";
import type { UserRef } from "./types";

const users: UserRef[] = [
  { id: 1, name: "Klaus Klein", email: "klaus.klein@example.test", jobTitle: null },
  { id: 2, name: "Maria Klein", email: "maria.klein@example.test", jobTitle: null },
  { id: 3, name: "Maria Becker", email: "m.becker@example.test", jobTitle: null },
];

describe("mentionFor", () => {
  it("nimmt den Nachnamen, wenn er eindeutig ist", () => {
    expect(mentionFor(users[2], users)).toBe("Becker");
  });

  it("weicht bei gleichen Nachnamen auf ein eindeutiges Kürzel aus", () => {
    const mention = mentionFor(users[1], users);
    expect(matchUser(users, mention)?.id).toBe(2);
  });

  it("trifft jede Person eindeutig", () => {
    for (const u of users) expect(matchUser(users, mentionFor(u, users))?.id).toBe(u.id);
  });
});
