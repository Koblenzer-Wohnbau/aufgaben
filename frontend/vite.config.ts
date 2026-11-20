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
    chunkSizeWarningLimit: 1500,
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
