// The compatibility gate (workspace `cmem/layout-and-packaging.md` § "The compatibility gate";
// this repo's `cmem/compat-gate.md`). "Backward compatible" is a gate result, never a judgment
// made at bump time.
//
//   bun scripts/compat.ts surface [--check]      update / verify PublicAPI.*.txt (Rule 1)
//   bun scripts/compat.ts report                 floor + previous-tag tests vs the last v* tag
//   bun scripts/compat.ts bump <patch|minor|major>
//   bun scripts/compat.ts release                CI, on the tagged commit
//   bun scripts/compat.ts selftest               pure checks: no git, build or network
//
// Rule 1's surface is declared by Microsoft.CodeAnalysis.PublicApiAnalyzers: PublicAPI.Shipped.txt
// (as of the last release) + PublicAPI.Unshipped.txt (since). The compiler fails the build when the
// code disagrees with them (RS0016/RS0017/RS0036/RS0037 are errors in the csproj), so a stale
// surface cannot be built. This script diffs those lines plus the README's SPEC version.
//
// Child `dotnet` processes use $UWL_DOTNET, else `dotnet10` when it is on PATH, else `dotnet`.

import { cpSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, relative, sep } from "node:path";

export class GateError extends Error {}

export type Level = "patch" | "minor" | "major";
const RANK: Record<Level, number> = { patch: 0, minor: 1, major: 2 };
const maxLevel = (a: Level, b: Level): Level => (RANK[a] >= RANK[b] ? a : b);

// ---------------------------------------------------------------- SemVer

export type SemVer = { major: number; minor: number; patch: number };

export function parseSemVer(s: string): SemVer {
  const m = /^v?(\d+)\.(\d+)\.(\d+)$/.exec(s.trim());
  if (!m) throw new GateError(`not a plain SemVer version: '${s}'`);
  return { major: Number(m[1]), minor: Number(m[2]), patch: Number(m[3]) };
}

export const fmt = (v: SemVer) => `${v.major}.${v.minor}.${v.patch}`;

export const compare = (a: SemVer, b: SemVer) => a.major - b.major || a.minor - b.minor || a.patch - b.patch;

export function next(v: SemVer, kind: Level): SemVer {
  if (kind === "major") return { major: v.major + 1, minor: 0, patch: 0 };
  if (kind === "minor") return { major: v.major, minor: v.minor + 1, patch: 0 };
  return { major: v.major, minor: v.minor, patch: v.patch + 1 };
}

// The level a release from `from` to `to` claims; `to` must be greater.
export function step(from: SemVer, to: SemVer): Level {
  if (compare(to, from) <= 0) throw new GateError(`${fmt(to)} is not above ${fmt(from)}`);
  return to.major > from.major ? "major" : to.minor > from.minor ? "minor" : "patch";
}

// ---------------------------------------------------------------- Rule 1: the surface

export type Surface = { spec: string; symbols: Set<string> };

const REMOVED = "*REMOVED*";

// The declared API: Shipped plus Unshipped, minus Unshipped's `*REMOVED*` lines (the analyzer's
// convention for removing a shipped symbol).
export function declaredApi(shipped: string, unshipped: string): Set<string> {
  const lines = (text: string) =>
    text.replace(/\r\n/g, "\n").split("\n").map((l) => l.trim()).filter((l) => l !== "" && !l.startsWith("#"));
  const api = new Set(lines(shipped));
  for (const line of lines(unshipped)) {
    if (line.startsWith(REMOVED)) api.delete(line.slice(REMOVED.length));
    else api.add(line);
  }
  return api;
}

// Release: Unshipped folds into Shipped, and Unshipped starts over. Shipped keeps its own order
// (the analyzer's fixer writes it that way); removed lines drop out, new ones are appended, so a
// release with no API change leaves Shipped byte-identical.
export function foldUnshipped(shipped: string, unshipped: string): { shipped: string; unshipped: string } {
  const api = declaredApi(shipped, unshipped);
  const kept = [...declaredApi(shipped, "")].filter((l) => api.has(l));
  const added = [...api].filter((l) => !kept.includes(l));
  return { shipped: `#nullable enable\n${[...kept, ...added].join("\n")}\n`, unshipped: "#nullable enable\n" };
}

export function specVersion(readme: string): string {
  const matches = [...readme.matchAll(/\*\*SPEC\.md v(\d+\.\d+\.\d+)\*\*/g)];
  if (matches.length !== 1) {
    throw new GateError(`README must declare '**SPEC.md vX.Y.Z**' exactly once (found ${matches.length})`);
  }
  return matches[0][1];
}

// ---------------------------------------------------------------- Rule 2: the floor

export type Floor = { level: Level; reasons: string[] };

// Removed (or changed: a changed line is removed + added) → major; added → minor; nothing → patch.
// The SPEC version moves it by its own level; lowered → major.
export function floorOf(before: Surface, after: Surface): Floor {
  let level: Level = "patch";
  const reasons: string[] = [];
  const raise = (l: Level, why: string) => {
    level = maxLevel(level, l);
    reasons.push(why);
  };
  for (const s of [...before.symbols].sort()) if (!after.symbols.has(s)) raise("major", `removed: ${s}`);
  for (const s of [...after.symbols].sort()) if (!before.symbols.has(s)) raise("minor", `added: ${s}`);
  const [sb, sa] = [parseSemVer(before.spec), parseSemVer(after.spec)];
  if (compare(sa, sb) < 0) raise("major", `SPEC lowered: ${fmt(sb)} → ${fmt(sa)}`);
  else if (compare(sa, sb) > 0) raise(step(sb, sa), `SPEC raised: ${fmt(sb)} → ${fmt(sa)}`);
  return { level, reasons };
}

export type TestResult = { green: boolean; summary: string };

// Everything that blocks a release from `last` to `target`. Rule 6: below 1.0.0 a minor is allowed
// where the floor says major, but the Breaking Changes row is still mandatory.
export function problems(last: SemVer, target: SemVer, floor: Floor, breakingRow: boolean, previous: TestResult): string[] {
  const out: string[] = [];
  const claimed = step(last, target);
  const effective: Level = last.major === 0 && floor.level === "major" ? "minor" : floor.level;
  if (RANK[claimed] < RANK[effective]) {
    const note = effective !== floor.level ? ", minor under the 0.x rule" : "";
    out.push(`${claimed} ${fmt(last)} → ${fmt(target)} is below the floor (${floor.level}${note})`);
  }
  if (floor.level === "major" && !breakingRow) {
    out.push("the floor is major: README '### ⚠️ Breaking Changes' needs a row for this release");
  }
  if (floor.level !== "major" && !previous.green) out.push(`previous-tag tests are not green: ${previous.summary}`);
  return out;
}

// ---------------------------------------------------------------- README (Rule 4)

export const NEXT_RELEASE = "_next release_";

function breakingSection(readme: string): [number, number] | null {
  const heading = /^###\s+⚠️\s*Breaking Changes\s*$/m.exec(readme);
  if (!heading) return null;
  const start = heading.index + heading[0].length;
  const rest = /^#{1,3}\s/m.exec(readme.slice(start));
  return [start, rest ? start + rest.index : readme.length];
}

export function hasRow(readme: string, label: string): boolean {
  const section = breakingSection(readme);
  if (!section) return false;
  return readme.slice(section[0], section[1]).split("\n").some((l) => l.trimStart().startsWith(`| ${label} |`));
}

export function stampNextRelease(readme: string, version: SemVer): string {
  const section = breakingSection(readme);
  if (!section || !hasRow(readme, NEXT_RELEASE)) {
    throw new GateError(`no '| ${NEXT_RELEASE} |' row under '### ⚠️ Breaking Changes'`);
  }
  const [start, end] = section;
  const body = readme.slice(start, end).replaceAll(`| ${NEXT_RELEASE} |`, () => `| ${fmt(version)} |`);
  return readme.slice(0, start) + body + readme.slice(end);
}

// ---------------------------------------------------------------- the version line

export function readVersion(csproj: string): SemVer {
  const matches = [...csproj.matchAll(/<Version>([^<]*)<\/Version>/g)];
  if (matches.length !== 1) throw new GateError(`the csproj must hold exactly one <Version> (found ${matches.length})`);
  return parseSemVer(matches[0][1]);
}

export function writeVersion(csproj: string, version: SemVer): string {
  readVersion(csproj);
  return csproj.replace(/<Version>[^<]*<\/Version>/, () => `<Version>${fmt(version)}</Version>`);
}

// ---------------------------------------------------------------- test output (workspace 1.1)

// Green needs exit 0, at least one pass and no failures: the OUTPUT counts, not the exit code.
export function judgeTests(output: string, exitCode: number): TestResult {
  let passed = 0;
  let failed = 0;
  let runs = 0;
  for (const m of output.matchAll(/Failed:\s+(\d+),\s+Passed:\s+(\d+)/g)) {
    failed += Number(m[1]);
    passed += Number(m[2]);
    runs++;
  }
  const summary = runs === 0 ? `no test counts in the output (exit ${exitCode})` : `${passed} passed, ${failed} failed (exit ${exitCode})`;
  return { green: exitCode === 0 && runs > 0 && passed > 0 && failed === 0, summary };
}

// ---------------------------------------------------------------- processes

function run(cmd: string[], cwd: string): { code: number; output: string } {
  const p = Bun.spawnSync({
    cmd,
    cwd,
    stdout: "pipe",
    stderr: "pipe",
    env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: "en", DOTNET_NOLOGO: "1" },
  });
  return { code: p.exitCode ?? 1, output: p.stdout.toString() + p.stderr.toString() };
}

const tail = (text: string, n: number) => text.trimEnd().split("\n").slice(-n).join("\n");

function must(cmd: string[], cwd: string): string {
  const r = run(cmd, cwd);
  if (r.code !== 0) throw new GateError(`${cmd.join(" ")} failed (exit ${r.code}):\n${tail(r.output, 25)}`);
  return r.output;
}

const git = (cwd: string, ...args: string[]) => must(["git", ...args], cwd).trim();

let dotnetExe: string | undefined;
function dotnet(): string {
  dotnetExe ??= process.env.UWL_DOTNET || (Bun.which("dotnet10") ? "dotnet10" : "dotnet");
  return dotnetExe;
}

// ---------------------------------------------------------------- the gate

const PROJECT = "UniversalWasmLoader.csproj";
const SHIPPED = "PublicAPI.Shipped.txt";
const UNSHIPPED = "PublicAPI.Unshipped.txt";

// Releases published before this gate existed (reviewed exceptions, 2026-10-22). v0.1.0 = commit
// d990087: its tree has no tests/ (gitignored then) and no PublicAPI files. Its surface is the
// first committed PublicAPI.Shipped.txt, generated from library sources byte-identical to v0.1.0's
// (`git diff d990087 <that commit> -- *.cs` at the root is empty; cmem/compat-gate.md).
const UNGATED = new Set(["v0.1.0"]);

const repoRoot = () => git(process.cwd(), "rev-parse", "--show-toplevel");
const read = (path: string) => readFileSync(path, "utf8");

function surfaceOfTree(dir: string): Surface {
  return {
    spec: specVersion(read(join(dir, "README.md"))),
    symbols: declaredApi(read(join(dir, SHIPPED)), read(join(dir, UNSHIPPED))),
  };
}

function baselineSurface(repo: string, tag: string, tree: string): Surface {
  if (existsSync(join(tree, SHIPPED))) return surfaceOfTree(tree);
  if (!UNGATED.has(tag)) throw new GateError(`${tag} has no ${SHIPPED}: no baseline surface`);
  const first = git(repo, "log", "--diff-filter=A", "--format=%H", "--", SHIPPED).split("\n").filter(Boolean).at(-1);
  if (!first) throw new GateError(`no commit adds ${SHIPPED}: cannot stand in for ${tag}`);
  return {
    spec: specVersion(read(join(tree, "README.md"))),
    symbols: declaredApi(git(repo, "show", `${first}:${SHIPPED}`), ""),
  };
}

function lastTag(repo: string, excludeHead: boolean): string | undefined {
  const lines = (s: string) => s.split("\n").map((t) => t.trim()).filter(Boolean);
  const atHead = new Set(excludeHead ? lines(git(repo, "tag", "--points-at", "HEAD", "--list", "v*")) : []);
  return lines(git(repo, "tag", "--merged", "HEAD", "--list", "v*"))
    .filter((t) => !atHead.has(t) && /^v\d+\.\d+\.\d+$/.test(t))
    .sort((a, b) => compare(parseSemVer(b), parseSemVer(a)))[0];
}

// The PublicAPI analyzer's errors in build output, one per symbol: the message without MSBuild's
// trailing help link and `[project::TargetFramework=…]` (a symbol may itself contain `[]`).
export function analyzerFindings(output: string): string[] {
  const found = new Set<string>();
  for (const line of output.split(/\r?\n/)) {
    const m = /error (RS00(?:16|17|36|37)): (.*)$/.exec(line);
    if (!m) continue;
    const message = m[2].replace(/\s*\[[^\]]*::TargetFramework=[^\]]*\]\s*$/, "").replace(/\s*\(https?:\/\/[^)]*\)\s*$/, "");
    found.add(`${m[1]}: ${message.trim()}`);
  }
  return [...found];
}

// Rule 1 enforcement: the build fails on any RS0016/RS0017/RS0036/RS0037 (csproj WarningsAsErrors).
// `--no-incremental`: the analyzer only reports when the compiler runs, and on exFAT (`D:`) an
// incremental build has been seen to reuse stale output after a source edit (2026-10-22).
function buildCheck(repo: string): { ok: boolean; findings: string[] } {
  const r = run([dotnet(), "build", join(repo, PROJECT), "-c", "Release", "--no-incremental"], repo);
  const findings = analyzerFindings(r.output);
  if (r.code !== 0 && findings.length === 0) throw new GateError(`the build failed for another reason:\n${tail(r.output, 25)}`);
  return { ok: r.code === 0, findings };
}

// Rule 3: the previous tag's tests/ (test code AND fixtures, unmodified) against the CURRENT library.
function previousTests(repo: string, tree: string, tag: string): TestResult {
  const tests = join(tree, "tests");
  if (!existsSync(tests)) {
    return UNGATED.has(tag)
      ? { green: true, summary: `exempt: ${tag} predates the gate and its commit has no tests/ (reviewed exception)` }
      : { green: false, summary: `${tag} has no tests/ to run` };
  }
  const work = mkdtempSync(join(tmpdir(), "uwl-prevtests-"));
  try {
    const copy = join(work, "tests");
    cpSync(tests, copy, {
      recursive: true,
      filter: (src) => !relative(tests, src).split(sep).some((seg) => seg === "bin" || seg === "obj"),
    });
    const csprojName = [...new Bun.Glob("*.csproj").scanSync(copy)];
    if (csprojName.length !== 1) throw new GateError(`${tag}'s tests/ must hold exactly one .csproj`);
    const csprojPath = join(copy, csprojName[0]);
    const text = read(csprojPath);
    const reference = /Include="[^"]*UniversalWasmLoader\.csproj"/g;
    if ((text.match(reference) ?? []).length !== 1) {
      throw new GateError(`${tag}'s test project must reference UniversalWasmLoader.csproj exactly once`);
    }
    writeFileSync(csprojPath, text.replace(reference, () => `Include="${join(repo, PROJECT)}"`));
    const r = run([dotnet(), "test", copy], work);
    const result = judgeTests(r.output, r.code);
    if (!result.green) console.log(tail(r.output, 15));
    return result;
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
}

type Evaluation = { tag: string; floor: Floor; previous: TestResult; stale: string[] };

function evaluate(repo: string, excludeHead: boolean): Evaluation {
  const tag = lastTag(repo, excludeHead);
  if (!tag) throw new GateError("no v* tag is reachable from HEAD: nothing to compare against");
  const check = buildCheck(repo);
  const stale = check.ok ? [] : check.findings.map((f) => `the PublicAPI files disagree with the code: ${f}`);
  const current = surfaceOfTree(repo);

  const tree = mkdtempSync(join(tmpdir(), "uwl-tag-"));
  rmSync(tree, { recursive: true, force: true });
  git(repo, "worktree", "add", "--detach", tree, tag);
  try {
    const floor = floorOf(baselineSurface(repo, tag, tree), current);
    console.log(`last tag ${tag}; floor ${floor.level}${floor.reasons.length === 0 ? " (surface unchanged)" : ":"}`);
    for (const r of floor.reasons) console.log(`  ${r}`);
    const previous = previousTests(repo, tree, tag);
    console.log(`previous-tag tests (${tag}'s tests/ against the current source): ${previous.green ? "GREEN" : "RED"}, ${previous.summary}`);
    return { tag, floor, previous, stale };
  } finally {
    run(["git", "worktree", "remove", "--force", tree], repo);
    rmSync(tree, { recursive: true, force: true });
  }
}

function cmdSurface(check: boolean): number {
  const repo = repoRoot();
  if (!check) {
    // Adds new public symbols to Unshipped; a removed symbol is reported by the build (RS0017) and
    // its line is deleted (or marked *REMOVED* in Unshipped when it had shipped) by hand.
    must([dotnet(), "format", "analyzers", join(repo, PROJECT), "--diagnostics", "RS0016", "RS0037", "--severity", "info"], repo);
  }
  const r = buildCheck(repo);
  if (r.ok) {
    console.log(`${SHIPPED} + ${UNSHIPPED} match the code (${surfaceOfTree(repo).symbols.size} symbols, SPEC ${surfaceOfTree(repo).spec})`);
    return 0;
  }
  console.log(`STALE: the PublicAPI files disagree with the code (Rule 5: re-baseline in its own commit):`);
  for (const f of r.findings) console.log(`  ${f}`);
  return 1;
}

function cmdReport(): number {
  const e = evaluate(repoRoot(), false);
  for (const s of e.stale) console.log(`  ${s}`);
  return e.stale.length === 0 ? 0 : 1;
}

function cmdBump(kindArg: string | undefined): number {
  if (kindArg !== "patch" && kindArg !== "minor" && kindArg !== "major") throw new GateError("bump needs patch, minor or major");
  const repo = repoRoot();
  if (git(repo, "status", "--porcelain") !== "") throw new GateError("the working tree is not clean");

  const csprojPath = join(repo, PROJECT);
  const csproj = read(csprojPath);
  const line = readVersion(csproj);
  const e = evaluate(repo, false);
  const tagVersion = parseSemVer(e.tag);
  const target = next(tagVersion, kindArg);
  const readmePath = join(repo, "README.md");
  const readme = read(readmePath);
  const found = [...e.stale];
  if (compare(line, tagVersion) !== 0) found.push(`the version line (${fmt(line)}) must equal the last tag (${e.tag}) before a bump`);
  found.push(...problems(tagVersion, target, e.floor, hasRow(readme, NEXT_RELEASE), e.previous));

  if (found.length > 0) {
    console.log(`REFUSED: bump ${kindArg} ${fmt(tagVersion)} → ${fmt(target)}`);
    for (const p of found) console.log(`  ${p}`);
    return 1;
  }
  writeFileSync(csprojPath, writeVersion(csproj, target));
  const folded = foldUnshipped(read(join(repo, SHIPPED)), read(join(repo, UNSHIPPED)));
  writeFileSync(join(repo, SHIPPED), folded.shipped);
  writeFileSync(join(repo, UNSHIPPED), folded.unshipped);
  if (e.floor.level === "major") writeFileSync(readmePath, stampNextRelease(readme, target));
  console.log(`bumped ${fmt(tagVersion)} → ${fmt(target)} (floor ${e.floor.level}); ${UNSHIPPED} folded into ${SHIPPED}.`);
  console.log(`Review, commit, then tag v${fmt(target)}; nothing was committed, tagged or pushed.`);
  return 0;
}

function cmdRelease(): number {
  const repo = repoRoot();
  const line = readVersion(read(join(repo, PROJECT)));
  const headTags = git(repo, "tag", "--points-at", "HEAD", "--list", "v*").split("\n").filter(Boolean);
  if (!headTags.includes(`v${fmt(line)}`)) {
    throw new GateError(`HEAD is not tagged v${fmt(line)} (the version line); tags here: ${headTags.join(", ") || "none"}`);
  }
  const e = evaluate(repo, true);
  const found = [...e.stale, ...problems(parseSemVer(e.tag), line, e.floor, hasRow(read(join(repo, "README.md")), fmt(line)), e.previous)];
  if (found.length === 0) {
    console.log(`release v${fmt(line)} passes the gate`);
    return 0;
  }
  console.log(`BLOCKED: release v${fmt(line)}`);
  for (const p of found) console.log(`  ${p}`);
  return 1;
}

// ---------------------------------------------------------------- self-test

function selftest(): number {
  const failed: string[] = [];
  let total = 0;
  const check = (name: string, ok: boolean) => {
    total++;
    if (!ok) failed.push(name);
  };
  const throws = (f: () => unknown) => {
    try {
      f();
      return false;
    } catch (e) {
      return e instanceof GateError;
    }
  };

  const S = (spec: string, ...symbols: string[]): Surface => ({ spec, symbols: new Set(symbols) });
  const base = S("3.0.0", "A", "A.F() -> int");
  check("floor: identical → patch", floorOf(base, base).level === "patch" && floorOf(base, base).reasons.length === 0);
  check("floor: symbol added → minor", floorOf(base, S("3.0.0", "A", "A.F() -> int", "A.G() -> void")).level === "minor");
  check("floor: symbol removed → major", floorOf(base, S("3.0.0", "A")).level === "major");
  check("floor: signature changed → major", floorOf(base, S("3.0.0", "A", "A.F() -> long")).level === "major");
  check("floor: SPEC patch raise → patch", floorOf(base, S("3.0.1", "A", "A.F() -> int")).level === "patch");
  check("floor: SPEC minor raise → minor", floorOf(base, S("3.1.0", "A", "A.F() -> int")).level === "minor");
  check("floor: SPEC major raise → major", floorOf(base, S("4.0.0", "A", "A.F() -> int")).level === "major");
  check("floor: SPEC lowered → major", floorOf(base, S("2.9.9", "A", "A.F() -> int")).level === "major");
  check("floor: reasons name the symbol", floorOf(base, S("3.0.0", "A")).reasons.includes("removed: A.F() -> int"));

  const v = parseSemVer;
  check("semver: parse with v", compare(v("v1.2.3"), { major: 1, minor: 2, patch: 3 }) === 0);
  check("semver: prerelease rejected", throws(() => v("1.2.3-rc.1")));
  check("semver: 1.10.0 > 1.9.9", compare(v("1.10.0"), v("1.9.9")) > 0);
  check("semver: next patch", fmt(next(v("0.1.0"), "patch")) === "0.1.1");
  check("semver: next minor resets patch", fmt(next(v("0.1.4"), "minor")) === "0.2.0");
  check("semver: next major resets both", fmt(next(v("1.4.4"), "major")) === "2.0.0");
  check("semver: step minor", step(v("0.1.0"), v("0.2.0")) === "minor");
  check("semver: step not increasing throws", throws(() => step(v("0.2.0"), v("0.2.0"))));

  const green = { green: true, summary: "ok" };
  const red = { green: false, summary: "1 failed" };
  const F = (level: Level): Floor => ({ level, reasons: [] });
  check("policy: patch at floor patch allowed", problems(v("1.0.0"), v("1.0.1"), F("patch"), false, green).length === 0);
  check("policy: patch at floor minor refused", problems(v("1.0.0"), v("1.0.1"), F("minor"), false, green).length === 1);
  check("policy: minor at floor minor allowed", problems(v("1.0.0"), v("1.1.0"), F("minor"), false, green).length === 0);
  check("policy: 1.x minor at floor major refused", problems(v("1.0.0"), v("1.1.0"), F("major"), true, green).length === 1);
  check("policy: 0.x minor at floor major + row allowed", problems(v("0.1.0"), v("0.2.0"), F("major"), true, green).length === 0);
  check("policy: 0.x minor at floor major, no row refused", problems(v("0.1.0"), v("0.2.0"), F("major"), false, green).length === 1);
  check("policy: 0.x patch at floor major refused", problems(v("0.1.0"), v("0.1.1"), F("major"), true, green).length === 1);
  check("policy: major at floor major + row allowed", problems(v("1.0.0"), v("2.0.0"), F("major"), true, green).length === 0);
  check("policy: major at floor major, no row refused", problems(v("1.0.0"), v("2.0.0"), F("major"), false, green).length === 1);
  check("policy: red previous tests block a patch", problems(v("1.0.0"), v("1.0.1"), F("patch"), false, red).length === 1);
  check("policy: red previous tests do not block at floor major", problems(v("1.0.0"), v("2.0.0"), F("major"), true, red).length === 0);
  check("policy: above the floor is allowed", problems(v("1.0.0"), v("1.1.0"), F("patch"), false, green).length === 0);

  const shipped = "#nullable enable\nA\nA.F() -> int\n";
  check("api: shipped lines, header skipped", [...declaredApi(shipped, "#nullable enable\n")].join("|") === "A|A.F() -> int");
  check("api: unshipped adds", declaredApi(shipped, "#nullable enable\nA.G() -> void\n").has("A.G() -> void"));
  check("api: *REMOVED* removes", !declaredApi(shipped, "#nullable enable\n*REMOVED*A.F() -> int\n").has("A.F() -> int"));
  check("api: CRLF tolerated", declaredApi("#nullable enable\r\nA\r\n", "").has("A"));
  const folded = foldUnshipped(shipped, "#nullable enable\nA.G() -> void\n*REMOVED*A.F() -> int\n");
  check("api: fold moves adds, applies removes", folded.shipped === "#nullable enable\nA\nA.G() -> void\n" && folded.unshipped === "#nullable enable\n");
  const unsorted = "#nullable enable\nstatic Z.M() -> void\nA\n";
  check("api: an empty fold leaves Shipped byte-identical", foldUnshipped(unsorted, "#nullable enable\n").shipped === unsorted);
  check("api: fold appends after the existing order", foldUnshipped(unsorted, "#nullable enable\nB\n").shipped === `${unsorted}B\n`);

  const readme =
    "# X\n\n- Conforms to **SPEC.md v3.0.0** (ABI)\n\n### ⚠️ Breaking Changes\n\n| Version | Change |\n| --- | --- |\n" +
    "| _next release_ | removed `F` costs $& and $$ |\n| 0.2.0 | renamed `G` |\n\n## License\n\n| _next release_ | decoy |\n";
  check("readme: spec read", specVersion(readme) === "3.0.0");
  check("readme: spec twice throws", throws(() => specVersion(`${readme}\n**SPEC.md v3.0.0**\n`)));
  check("readme: spec absent throws", throws(() => specVersion("# X\n")));
  check("readme: next-release row found", hasRow(readme, NEXT_RELEASE));
  check("readme: version row found", hasRow(readme, "0.2.0"));
  check("readme: missing version row", !hasRow(readme, "0.3.0"));
  check("readme: row outside the section ignored", !hasRow(readme.replace("| _next release_ | removed", "| x | removed"), NEXT_RELEASE));
  check("readme: no heading → no row", !hasRow("# X\n| _next release_ | y |\n", NEXT_RELEASE));
  const stamped = stampNextRelease(readme, v("0.3.0"));
  check("readme: stamp writes the version", hasRow(stamped, "0.3.0") && !hasRow(stamped, NEXT_RELEASE));
  check("readme: stamp leaves other sections alone", stamped.includes("| _next release_ | decoy |"));
  check("readme: stamp keeps $ sequences literal", stamped.includes("costs $& and $$"));
  check("readme: stamp without a row throws", throws(() => stampNextRelease(stamped, v("0.4.0"))));

  const csproj = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>";
  check("version line: read", fmt(readVersion(csproj)) === "0.1.0");
  check("version line: write", fmt(readVersion(writeVersion(csproj, v("0.2.0")))) === "0.2.0");
  check("version line: two throw", throws(() => readVersion(`${csproj}<Version>1.0.0</Version>`)));

  check("tests: green output", judgeTests("Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7", 0).green);
  check("tests: a failure is red", !judgeTests("Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7", 1).green);
  check("tests: exit 0 with a failure is red", !judgeTests("Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7", 0).green);
  check("tests: zero passed is red", !judgeTests("Passed!  - Failed:     0, Passed:     0, Skipped:     0, Total:     0", 0).green);
  check("tests: no counts is red", !judgeTests("Build FAILED.", 0).green);
  check("tests: counts summed over runs", judgeTests("Failed: 0, Passed: 3\nFailed: 0, Passed: 4", 0).summary.startsWith("7 passed"));

  const rs =
    "src\\WasmModule.cs(70,5): error RS0017: Symbol 'UniversalWasmLoader.WasmModule.Bind(string! name) -> System.Func<object?[]!, object?>!' is part of the declared API, but is either not public or could not be found (https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md) [src\\UniversalWasmLoader.csproj::TargetFramework=net8.0]";
  const findings = analyzerFindings(`${rs}\r\n${rs.replace("net8.0", "net10.0")}\nerror CS0103: unrelated`);
  check(
    "analyzer: full symbol kept through []",
    findings.length === 1 && findings[0] === "RS0017: Symbol 'UniversalWasmLoader.WasmModule.Bind(string! name) -> System.Func<object?[]!, object?>!' is part of the declared API, but is either not public or could not be found",
  );
  check("analyzer: other errors ignored", analyzerFindings("x.cs(1,1): error CS0103: The name 'y' does not exist").length === 0);

  for (const f of failed) console.log(`FAIL ${f}`);
  console.log(`selftest: ${total - failed.length}/${total} passed`);
  return failed.length === 0 ? 0 : 1;
}

// ---------------------------------------------------------------- main

if (import.meta.main) {
  const [command = "report", arg] = Bun.argv.slice(2);
  try {
    const code =
      command === "surface" ? cmdSurface(arg === "--check")
      : command === "report" ? cmdReport()
      : command === "bump" ? cmdBump(arg)
      : command === "release" ? cmdRelease()
      : command === "selftest" ? selftest()
      : (console.error("usage: compat.ts <surface [--check] | report | bump <patch|minor|major> | release | selftest>"), 2);
    process.exit(code);
  } catch (e) {
    if (!(e instanceof GateError)) throw e;
    console.error(`compat: ${e.message}`);
    process.exit(2);
  }
}
