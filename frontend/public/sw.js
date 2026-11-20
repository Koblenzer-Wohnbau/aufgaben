// Service Worker: macht die App installierbar und startet sie blitzschnell.
// Gecacht werden nur die Oberfläche und gebaute Dateien – NIE Daten aus /api, Anmeldung oder Kalender.
const CACHE = "aufgaben-v2";
const SHELL = ["/", "/favicon.svg", "/manifest.webmanifest", "/icon-192.png"];
const NEVER = ["/api/", "/auth/", "/signin-oidc", "/signout-callback-oidc", "/kalender/", "/healthz", "/readyz"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== "GET" || url.origin !== self.location.origin) return;
  if (NEVER.some((prefix) => url.pathname.startsWith(prefix))) return;

  // Gebaute Dateien haben einen Hash im Namen und ändern sich nie: Cache zuerst.
  if (url.pathname.startsWith("/assets/")) {
    event.respondWith(
      caches.open(CACHE).then(async (cache) => {
        const hit = await cache.match(request);
        if (hit) return hit;
        const response = await fetch(request);
        if (response.ok) cache.put(request, response.clone());
        return response;
      }),
    );
    return;
  }

  // Seitenaufrufe: Netz zuerst (immer aktuell), offline die zuletzt geladene Oberfläche.
  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request)
        .then((response) => {
          if (response.ok) {
            const copy = response.clone();
            caches.open(CACHE).then((cache) => cache.put("/", copy));
          }
          return response;
        })
        .catch(() => caches.match("/")),
    );
  }
});
