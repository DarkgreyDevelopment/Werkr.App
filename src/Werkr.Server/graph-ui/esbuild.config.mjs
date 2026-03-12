import { build } from "esbuild";

const isProd = process.env.NODE_ENV === "production";

await build({
  entryPoints: ["src/index.ts"],
  bundle: true,
  format: "esm",
  target: "es2022",
  outfile: "../wwwroot/js/dist/graph-ui.js",
  minify: isProd,
  sourcemap: isProd ? false : "linked",
  treeShaking: true,
  logLevel: "info",
});
