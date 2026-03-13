import { build } from "esbuild";

const isProd = process.env.NODE_ENV === "production";

const shared = {
  bundle: true,
  format: "esm",
  target: "es2022",
  minify: isProd,
  sourcemap: isProd ? false : "linked",
  treeShaking: true,
  logLevel: "info",
};

await Promise.all([
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
  build({
    ...shared,
    entryPoints: ["src/dag/dag-readonly.ts"],
    outfile: "../wwwroot/js/dist/dag-readonly.js",
  }),
]);
