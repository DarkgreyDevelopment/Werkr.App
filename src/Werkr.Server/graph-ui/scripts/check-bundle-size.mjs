/**
 * Bundle size budget check.
 * Reads esbuild metafile, calculates per-bundle gzipped sizes,
 * and fails if total gzipped output exceeds the budget threshold.
 */
import { readFileSync } from "node:fs";
import { gzipSync } from "node:zlib";
import { resolve, relative, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const BUDGET_BYTES = 500 * 1024; // 500 KB gzipped total
const DIST_DIR = resolve(__dirname, "../../wwwroot/js/dist");
const META_PATH = resolve(DIST_DIR, "meta.json");

let metafile;
try {
  metafile = JSON.parse(readFileSync(META_PATH, "utf-8"));
} catch (err) {
  console.error(`❌ Could not read metafile at ${META_PATH}`);
  console.error("   Run 'npm run build:prod' first to generate the metafile.");
  process.exit(1);
}

const outputs = metafile.outputs;
const rows = [];
let totalRaw = 0;
let totalGzipped = 0;

for (const [outputPath, info] of Object.entries(outputs)) {
  // Skip .map and .LEGAL.txt files
  if (outputPath.endsWith(".map") || outputPath.includes("LEGAL")) continue;

  const fullPath = resolve(__dirname, "..", outputPath);
  let rawBytes;
  try {
    rawBytes = readFileSync(fullPath).length;
  } catch {
    // File might be a metafile artifact; skip
    continue;
  }

  const gzippedBytes = gzipSync(readFileSync(fullPath)).length;
  const displayPath = relative(DIST_DIR, fullPath);

  rows.push({ name: displayPath, raw: rawBytes, gzipped: gzippedBytes });
  totalRaw += rawBytes;
  totalGzipped += gzippedBytes;
}

// Sort by gzipped size descending
rows.sort((a, b) => b.gzipped - a.gzipped);

// Print table
const toKB = (bytes) => (bytes / 1024).toFixed(1);
console.log("");
console.log("┌─────────────────────────────────────────┬──────────┬───────────┐");
console.log("│ Bundle                                  │ Raw (KB) │ Gzip (KB) │");
console.log("├─────────────────────────────────────────┼──────────┼───────────┤");

for (const row of rows) {
  const name = row.name.padEnd(39);
  const raw = toKB(row.raw).padStart(8);
  const gz = toKB(row.gzipped).padStart(9);
  console.log(`│ ${name} │ ${raw} │ ${gz} │`);
}

console.log("├─────────────────────────────────────────┼──────────┼───────────┤");
const totalLabel = "TOTAL".padEnd(39);
const totalRawStr = toKB(totalRaw).padStart(8);
const totalGzStr = toKB(totalGzipped).padStart(9);
console.log(`│ ${totalLabel} │ ${totalRawStr} │ ${totalGzStr} │`);
console.log("└─────────────────────────────────────────┴──────────┴───────────┘");
console.log("");

const budgetKB = toKB(BUDGET_BYTES);
if (totalGzipped > BUDGET_BYTES) {
  console.error(`❌ Bundle budget EXCEEDED: ${toKB(totalGzipped)} KB gzipped > ${budgetKB} KB budget`);
  process.exit(1);
} else {
  const remaining = toKB(BUDGET_BYTES - totalGzipped);
  console.log(`✅ Bundle budget OK: ${toKB(totalGzipped)} KB / ${budgetKB} KB (${remaining} KB remaining)`);
}
