import path from "node:path";
import { defineConfig } from "vitest/config";

/**
 * Wave-1 rule (grilled Q1): tests run in plain node against the DEEPENED
 * modules' interfaces — no DOM. Anything needing a browser is Playwright
 * territory (the F-gate suite), not this runner.
 */
export default defineConfig({
  resolve: {
    alias: { "@": path.resolve(__dirname, "src") },
  },
  test: {
    include: ["src/**/*.test.ts"],
    environment: "node",
  },
});
