import { app, authentication, pages } from "@microsoft/teams-js";

/** Die App läuft in Teams, wenn das Manifest "?inTeams=1" an die Adresse hängt. */
export const inTeams = new URLSearchParams(window.location.search).has("inTeams");

/** Kanal-Tab: kompakte Ansicht ohne Seitenleiste. */
export const inChannelTab = new URLSearchParams(window.location.search).has("kanal");

export type TeamsTheme = "default" | "dark" | "contrast";

export interface TeamsInfo {
  theme: TeamsTheme;
  subPageId?: string;
  channelId?: string;
  channelName?: string;
  teamId?: string;
  teamName?: string;
  frameContext?: string;
}

export async function initTeams(): Promise<TeamsInfo> {
  await app.initialize();
  const ctx = await app.getContext();
  app.notifySuccess();
  return {
    theme: (ctx.app.theme as TeamsTheme) ?? "default",
    subPageId: ctx.page.subPageId,
    channelId: ctx.channel?.id,
    channelName: ctx.channel?.displayName,
    teamId: ctx.team?.groupId,
    teamName: ctx.team?.displayName,
    frameContext: ctx.page.frameContext,
  };
}

export function onTeamsThemeChange(handler: (theme: TeamsTheme) => void) {
  app.registerOnThemeChangeHandler((t) => handler(t as TeamsTheme));
}

let cached: { token: string; exp: number } | null = null;

function expiry(token: string): number {
  try {
    const payload = JSON.parse(atob(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
    return (payload.exp ?? 0) * 1000;
  } catch {
    return 0;
  }
}

/** Teams-SSO-Token (Audience = eigene App). Wird bis kurz vor Ablauf wiederverwendet. */
export async function teamsToken(): Promise<string> {
  if (cached && cached.exp - Date.now() > 120_000) return cached.token;
  const token = await authentication.getAuthToken();
  cached = { token, exp: expiry(token) };
  return token;
}

/** Konfigurationsseite eines Kanal-Tabs: speichert, welches Projekt der Tab zeigt. */
export function registerChannelTabSave(save: () => Promise<{ projectId: number; label: string }>) {
  pages.config.registerOnSaveHandler(async (event) => {
    try {
      const { projectId, label } = await save();
      const origin = window.location.origin;
      await pages.config.setConfig({
        entityId: `projekt-${projectId}`,
        contentUrl: `${origin}/?inTeams=1&kanal=1#/projekt/${projectId}`,
        websiteUrl: `${origin}/#/projekt/${projectId}`,
        suggestedDisplayName: `Aufgaben ${label}`.slice(0, 50),
      });
      event.notifySuccess();
    } catch (e) {
      event.notifyFailure(e instanceof Error ? e.message : "Speichern fehlgeschlagen");
    }
  });
}

export function setChannelTabValid(valid: boolean) {
  pages.config.setValidityState(valid);
}
