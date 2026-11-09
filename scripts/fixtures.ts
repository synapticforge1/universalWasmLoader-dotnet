// The fixtures in `tests/` are exactly the stamped reference set from universalWasmLoader-js
// (workspace letter H7 / S4; SPEC §7: "without modification"). Same rule as -js's
// `scripts/check-fixtures.ts`: fails when a stamped file is missing or its bytes differ from
// `tests/fixtures.json`, or when `tests/` holds a `.wasm` / `.wit` / `.wat` the stamp does not list.
//
//   bun scripts/fixtures.ts [dir]      dir defaults to tests/ (another dir is for proving the check)

import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

type Stamp = { generator: { describe: string; commit: string }; files: Record<string, string> };

export function fixtureProblems(dir: string): { stamp: Stamp; problems: string[] } {
  const stamp = JSON.parse(readFileSync(join(dir, "fixtures.json"), "utf8")) as Stamp;
  const problems: string[] = [];
  for (const [file, hash] of Object.entries(stamp.files)) {
    const p = join(dir, file);
    if (!existsSync(p)) problems.push(`${file}: missing`);
    else if (createHash("sha256").update(readFileSync(p)).digest("hex") !== hash) problems.push(`${file}: bytes differ from the stamp`);
  }
  for (const f of readdirSync(dir)) {
    if (/\.(wasm|wit|wat)$/.test(f) && !(f in stamp.files)) problems.push(`${f}: not in the stamp`);
  }
  return { stamp, problems };
}

if (import.meta.main) {
  const dir = Bun.argv[2] ?? fileURLToPath(new URL("../tests", import.meta.url));
  const { stamp, problems } = fixtureProblems(dir);
  if (problems.length > 0) {
    console.log(["Fixtures do not match tests/fixtures.json:", ...problems.map((p) => `  ${p}`)].join("\n"));
    console.log("Re-copy tests/*.wasm, *.wit, *.wat and fixtures.json from universalWasmLoader-js; never edit a fixture by hand.");
    process.exit(1);
  }
  console.log(
    `${Object.keys(stamp.files).length} fixtures match the stamp (wasmtk ${stamp.generator.describe}, ` +
      `${stamp.generator.commit.slice(0, 11)})`,
  );
}
