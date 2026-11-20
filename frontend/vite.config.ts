import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Im Entwicklungsmodus leitet Vite API-Aufrufe an das .NET-Backend weiter.
// Der Build landet direkt in wwwroot des Backends.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../src/Aufgaben.Api/wwwroot",
    emptyOutDir: true,
    sourcemap: false,
    target: "es2022",
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      output: {
        // Große, selten geänderte Bibliotheken in eigene Dateien: Der Browser behält sie
        // nach einem App-Update im Cache, neu geladen wird nur der eigene Code.
        manualChunks(id) {
          if (!id.includes("node_modules")) return undefined;
          // Icons und Komponenten verweisen gegenseitig aufeinander – getrennte Pakete würden
          // in falscher Reihenfolge initialisiert ("Cannot access before initialization").
          if (id.includes("@microsoft/teams-js")) return "teams";
          if (id.includes("@fluentui") || id.includes("@griffel") || id.includes("@floating-ui") || id.includes("tabster")) return "fluent";
          if (id.includes("react") || id.includes("scheduler") || id.includes("@tanstack")) return "react";
          return "vendor";
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "http://localhost:5080", changeOrigin: false },
      "/auth": { target: "http://localhost:5080", changeOrigin: false },
      "/kalender": { target: "http://localhost:5080", changeOrigin: false },
    },
  },
  test: {
    environment: "node",
  },
});
