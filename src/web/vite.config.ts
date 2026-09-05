import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
// From vitest rather than vite, so that the test section below is typed too.
import { defineConfig } from "vitest/config";

/**
 * The first path segment of everything the instance answers, and therefore
 * everything this application must not route itself.
 *
 * Three of them are the API and its documents (`docs/api.md`); the fourth is
 * `/device`, the one page the server renders on its own — it holds no session
 * and asks for a password every time
 * ([ADR 0008](../../docs/adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)),
 * so it is a page this application links to and never draws.
 *
 * Development runs the two toolchains side by side: Vite serves the SPA and
 * forwards these, so that the application reaches the instance at its own
 * origin there exactly as it does in the image.
 */
const instanceRoutes = ["/api", "/openapi", "/problems", "/device"];

export default defineConfig({
  plugins: [react(), tailwindcss()],

  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },

  // A local `npm run build` lands where the API serves static files from, so
  // that one `dotnet run` gives the whole product. The image does the same in
  // two stages.
  build: {
    outDir: "../Vaultaffe.Api/wwwroot",
    emptyOutDir: true,
  },

  server: {
    port: 5173,
    proxy: Object.fromEntries(instanceRoutes.map((route) => [route, "http://localhost:5142"])),
  },

  test: {
    environment: "jsdom",
    setupFiles: ["./src/shared/setupTests.ts"],
  },
});
