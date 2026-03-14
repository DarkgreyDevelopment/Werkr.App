import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: ["test/**/*.test.ts"],
    coverage: {
      provider: "v8",
      include: [
        "src/dag/changeset.ts",
        "src/dag/cycle-detection.ts",
        "src/dag/draft-storage.ts",
      ],
      thresholds: {
        lines: 90,
      },
    },
  },
});
