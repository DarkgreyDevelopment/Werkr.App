import { build } from "esbuild";
import { writeFileSync, mkdirSync } from "node:fs";

const isProd = process.env.NODE_ENV === "production";

const shared = {
  bundle: true,
  format: "esm",
  target: "es2022",
  minify: isProd,
  sourcemap: isProd ? false : "linked",
  treeShaking: true,
  legalComments: "external",
  metafile: true,
  logLevel: "info",
};

// Shared config for code-split DAG bundles
const dagShared = {
  ...shared,
  splitting: true,
  format: "esm",
  outdir: "../wwwroot/js/dist/dag",
  chunkNames: "chunk-[hash]",
};

const results = await Promise.all([
  // Standalone bundles (no splitting needed — no shared deps)
  build({
    ...shared,
    entryPoints: ["src/index.ts"],
    outfile: "../wwwroot/js/dist/graph-ui.js",
  }),
  build({
    ...shared,
    entryPoints: ["src/timeline/timeline-view.ts"],
    outfile: "../wwwroot/js/dist/timeline-view.js",
  }),
  // Code-split DAG bundles → shared vendor chunk
  build({
    ...dagShared,
    entryPoints: [
      "src/dag/dag-readonly.ts",
      "src/dag/dag-editor.ts",
    ],
  }),
]);

// Merge metafiles and write for bundle budget analysis
const mergedMeta = { inputs: {}, outputs: {} };
for (const result of results) {
  if (result.metafile) {
    Object.assign(mergedMeta.inputs, result.metafile.inputs);
    Object.assign(mergedMeta.outputs, result.metafile.outputs);
  }
}

mkdirSync("../wwwroot/js/dist", { recursive: true });
writeFileSync("../wwwroot/js/dist/meta.json", JSON.stringify(mergedMeta));
