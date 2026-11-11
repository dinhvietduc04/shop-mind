import path from "node:path";
import { defineConfig } from "vitest/config";

/**
 * Tests run in plain Node against module interfaces. No DOM. Tests that need a
 * browser are not part of this runner.
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
