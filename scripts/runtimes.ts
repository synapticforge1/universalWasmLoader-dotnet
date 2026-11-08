/**
 * The test suite must run on EVERY currently supported .NET line, LTS and STS (owner, 2026-11-07:
 * "like the -js version, does this project cover all of the current dotnet lts and sts versions
 * available?" → "yes"). Modelled on loader-js's `scripts/check-lts.ts`.
 *
 * Which lines: Microsoft's own release index (`releases-index.json`), every channel whose support
 * phase is `active` or `maintenance` and whose end-of-support date has not passed. That is decided
 * by the date, never by a list here, so a line ENTERS this check on its GA (its phase becomes
 * `active`; a `go-live` release candidate is not yet required) and LEAVES it after its EOL date.
 *
 * What it checks: each supported line is (1) a target of the test project
 * (`tests/UniversalWasmLoader.Tests.csproj` `<TargetFrameworks>`, as `net<major>.0`) and (2) an SDK
 * that CI installs (`.github/workflows/validate.yml`, the `actions/setup-dotnet` step's
 * `dotnet-version`, as `<major>.0.x`). A missing line fails, naming the file to change. A target
 * that is no longer supported is reported, not failed: the package may still ship that asset.
 *
 * ```sh
 * bun scripts/runtimes.ts            # the check (fetches the index)
 * bun scripts/runtimes.ts selftest   # pure-function checks, no network
 * ```
 */
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import process from "node:process";

const INDEX = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";
const ROOT = join(dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")), "..");
const TEST_PROJECT = "tests/UniversalWasmLoader.Tests.csproj";
const VALIDATE = ".github/workflows/validate.yml";

/** One channel of Microsoft's release index (only the fields used here). */
export interface Channel {
  "channel-version": string;
  "release-type": string;
  "support-phase": string;
  "eol-date"?: string | null;
}

/** The supported channels on `today` (YYYY-MM-DD): phase active / maintenance, EOL not passed. */
export function supportedLines(channels: Channel[], today: string): { major: number; type: string; eol: string }[] {
  return channels
    .filter((c) => c["support-phase"] === "active" || c["support-phase"] === "maintenance")
    .filter((c) => !c["eol-date"] || today <= c["eol-date"])
    .map((c) => ({ major: majorOf(c["channel-version"]), type: c["release-type"], eol: c["eol-date"] ?? "none" }))
    .sort((a, b) => a.major - b.major);
}

function majorOf(version: string): number {
  const m = /^(\d+)\.\d+$/.exec(version.trim());
  if (!m) throw new Error(`unexpected channel-version "${version}"`);
  return Number(m[1]);
}

/** The `net<N>.0` majors in a csproj's `<TargetFramework(s)>`. */
export function testTargets(csproj: string): number[] {
  const m = /<TargetFrameworks?>([^<]*)<\/TargetFrameworks?>/.exec(csproj);
  if (!m) throw new Error(`${TEST_PROJECT} has no <TargetFramework(s)>`);
  return m[1]!.split(";").map((t) => t.trim()).filter(Boolean).map((t) => {
    const n = /^net(\d+)\.0$/.exec(t);
    if (!n) throw new Error(`${TEST_PROJECT}: unexpected target "${t}"`);
    return Number(n[1]);
  });
}

/** The `<N>.0.x` majors of every `actions/setup-dotnet` step in a workflow. */
export function ciSdks(workflowYaml: string): number[] {
  const wf = Bun.YAML.parse(workflowYaml) as { jobs?: Record<string, { steps?: { uses?: string; with?: Record<string, unknown> }[] }> };
  const majors: number[] = [];
  for (const job of Object.values(wf.jobs ?? {})) {
    for (const step of job.steps ?? []) {
      if (!step.uses?.startsWith("actions/setup-dotnet@")) continue;
      for (const v of String(step.with?.["dotnet-version"] ?? "").split(/\s+/).filter(Boolean)) {
        const n = /^(\d+)\.0\.x$/.exec(v);
        if (!n) throw new Error(`${VALIDATE}: unexpected dotnet-version "${v}"`);
        majors.push(Number(n[1]));
      }
    }
  }
  return majors;
}

/** The problems (empty = pass) and the notes for one set of inputs. */
export function verdict(supported: { major: number }[], targets: number[], sdks: number[]): { problems: string[]; notes: string[] } {
  const problems: string[] = [];
  const notes: string[] = [];
  if (supported.length === 0) problems.push("the release index lists no supported .NET line (index format changed?)");
  for (const { major } of supported) {
    if (!targets.includes(major)) problems.push(`.NET ${major}.0 is supported but not a test target: add net${major}.0 to ${TEST_PROJECT}`);
    if (!sdks.includes(major)) problems.push(`.NET ${major}.0 is supported but CI does not install it: add ${major}.0.x to ${VALIDATE}`);
  }
  for (const t of targets) {
    if (!supported.some((s) => s.major === t)) notes.push(`net${t}.0 is still a test target but no longer supported (kept while the package ships that asset?)`);
  }
  return { problems, notes };
}

async function check(): Promise<number> {
  const res = await fetch(INDEX);
  if (!res.ok) throw new Error(`HTTP ${res.status} for ${INDEX}`);
  const index = (await res.json()) as { "releases-index": Channel[] };
  const today = new Date().toISOString().slice(0, 10);
  const supported = supportedLines(index["releases-index"], today);
  const targets = testTargets(readFileSync(join(ROOT, TEST_PROJECT), "utf8"));
  const sdks = ciSdks(readFileSync(join(ROOT, VALIDATE), "utf8"));
  console.log(`supported on ${today}: ${supported.map((s) => `${s.major}.0 ${s.type.toUpperCase()} (EOL ${s.eol})`).join(", ")}`);
  console.log(`test targets: ${targets.map((t) => `net${t}.0`).join(", ")}; CI SDKs: ${sdks.map((s) => `${s}.0.x`).join(", ")}`);
  const { problems, notes } = verdict(supported, targets, sdks);
  for (const n of notes) console.log(`note: ${n}`);
  for (const p of problems) console.error(`FAIL: ${p}`);
  if (problems.length === 0) console.log("every supported .NET line is a test target and installed in CI");
  return problems.length === 0 ? 0 : 1;
}

function selftest(): number {
  let passed = 0;
  let failed = 0;
  const ok = (name: string, cond: boolean) => {
    if (cond) passed++;
    else {
      failed++;
      console.error(`selftest FAILED: ${name}`);
    }
  };
  const throws = (f: () => unknown) => {
    try {
      f();
      return false;
    } catch {
      return true;
    }
  };
  const index: Channel[] = [
    { "channel-version": "11.0", "release-type": "sts", "support-phase": "go-live" },
    { "channel-version": "10.0", "release-type": "lts", "support-phase": "active", "eol-date": "2028-11-14" },
    { "channel-version": "9.0", "release-type": "sts", "support-phase": "maintenance", "eol-date": "2026-11-10" },
    { "channel-version": "8.0", "release-type": "lts", "support-phase": "maintenance", "eol-date": "2026-11-10" },
    { "channel-version": "7.0", "release-type": "sts", "support-phase": "eol", "eol-date": "2024-05-14" },
  ];
  const majors = (d: string) => supportedLines(index, d).map((s) => s.major).join(",");
  ok("lines on 2026-11-07", majors("2026-11-07") === "8,9,10");
  ok("the EOL day itself is still supported", majors("2026-11-10") === "8,9,10");
  ok("lines leave the day after EOL", majors("2026-11-11") === "10");
  ok("go-live (RC) is not required", !majors("2026-11-07").includes("11"));
  ok("GA enters", supportedLines([{ ...index[0]!, "support-phase": "active" }], "2026-11-20").length === 1);
  ok("bad channel-version throws", throws(() => supportedLines([{ ...index[1]!, "channel-version": "ten" }], "2026-11-07")));

  ok("csproj one target", testTargets("<TargetFramework>net10.0</TargetFramework>").join() === "10");
  ok("csproj many targets", testTargets("<TargetFrameworks>net8.0; net9.0;net10.0</TargetFrameworks>").join() === "8,9,10");
  ok("csproj odd target throws", throws(() => testTargets("<TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>")));
  ok("csproj without targets throws", throws(() => testTargets("<Project/>")));

  const yaml = "jobs:\n  v:\n    steps:\n      - uses: actions/checkout@abc\n      - uses: actions/setup-dotnet@def # v4\n        with:\n          dotnet-version: |\n            8.0.x\n            10.0.x\n";
  ok("ci sdks", ciSdks(yaml).join() === "8,10");
  ok("ci odd sdk throws", throws(() => ciSdks(yaml.replace("8.0.x", "8.x"))));

  const sup = [{ major: 8 }, { major: 9 }, { major: 10 }];
  ok("all covered passes", verdict(sup, [8, 9, 10], [8, 9, 10]).problems.length === 0);
  const missing = verdict(sup, [8, 10], [8, 10]).problems;
  ok("a missing line fails twice, naming both files", missing.length === 2 && missing[0]!.includes(TEST_PROJECT) && missing[1]!.includes(VALIDATE));
  const extra = verdict([{ major: 10 }], [8, 10], [8, 10]);
  ok("an unsupported target is a note, not a failure", extra.problems.length === 0 && extra.notes.length === 1);
  ok("an empty index fails", verdict([], [10], [10]).problems.length === 1);

  console.log(`selftest: ${passed}/${passed + failed} passed`);
  return failed === 0 ? 0 : 1;
}

const cmd = process.argv[2];
try {
  process.exit(cmd === "selftest" ? selftest() : cmd === undefined ? await check() : (console.error("usage: runtimes.ts [selftest]"), 2));
} catch (e) {
  console.error(`runtimes: ${(e as Error).message}`);
  process.exit(1);
}
