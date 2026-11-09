/**
 * The test suite must run on EVERY currently supported .NET line, LTS and STS (owner, 2026-11-07:
 * "like the -js version, does this project cover all of the current dotnet lts and sts versions
 * available?" → "yes"). Modelled on loader-js's `scripts/check-lts.ts`.
 *
 * Which lines: Microsoft's own release index (`releases-index.json`), every channel whose support
 * phase is `active` or `maintenance` and whose end-of-support date has not passed. That is decided
 * by the date, never by a list here, so a line ENTERS this check on its GA (its phase becomes
 * `active`; a `go-live` release candidate is not yet required) and LEAVES it once it is within
 * LEAVING_DAYS (30) of its end-of-support date: a leaving line is reported, not required (owner,
 * 2026-11-07: "I think we need to move to dotnet 10 since the EOL for 8 and 9 are this coming
 * week" — the package is .NET 10 only from 0.2.0).
 *
 * What it checks: each supported line is (1) a target of the test project
 * (`tests/UniversalWasmLoader.Tests.csproj` `<TargetFrameworks>`, as `net<major>.0`) and (2) an SDK
 * that EVERY CI job running `dotnet test` installs (in each `.github/workflows/*.yml`, that job's
 * `actions/setup-dotnet` `dotnet-version`, as `<major>.0.x`); a job that never runs the tests
 * (e.g. the push-only `publish` job) is not checked. A missing line fails, naming the file and job.
 * A target that is no longer supported is reported, not failed: the package may still ship it.
 * So is a target AHEAD of its GA (a go-live / preview line, e.g. net11.0 on the 11 RC; the test
 * project adds it only when built with SDK 11+). Every `<TargetFramework(s)>` element counts,
 * conditional ones included.
 *
 * ```sh
 * bun scripts/runtimes.ts            # the check (fetches the index)
 * bun scripts/runtimes.ts selftest   # pure-function checks, no network
 * ```
 *
 * Bun TypeScript (owner, 2026-11-09: "move the other scripts to bun"); ported back from
 * `runtimes.csx`, whose output it reproduces line for line (the workspace port rule).
 */
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import process from "node:process";

const INDEX = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";
const ROOT = join(dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")), "..");
const TEST_PROJECT = "tests/UniversalWasmLoader.Tests.csproj";
const WORKFLOWS = ".github/workflows";

/** The time limit for fetching Microsoft's index, request to last byte. */
const FETCH_SECONDS = 30;

/** A supported line this close to its end of support is "leaving": reported, not required. */
export const LEAVING_DAYS = 30;

/** Whether a line with end-of-support date `eol` (or "none") is leaving on `today`. */
export function isLeaving(eol: string, today: string): boolean {
  if (eol === "none") return false;
  const days = (Date.parse(`${eol}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000;
  return days <= LEAVING_DAYS;
}

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

/** The channels not yet released (phase go-live / preview): a target for one is "ahead of GA". */
export function upcomingLines(channels: Channel[]): number[] {
  return channels
    .filter((c) => c["support-phase"] === "go-live" || c["support-phase"] === "preview")
    .map((c) => majorOf(c["channel-version"]))
    .sort((a, b) => a - b);
}

/**
 * The `net<N>.0` majors in a csproj's `<TargetFramework(s)>` elements: ALL of them, conditional
 * ones included (the test project adds net11.0 only on SDK 11+, ahead of 11's GA), in order, each
 * major once.
 */
export function testTargets(csproj: string): number[] {
  const matches = [...csproj.matchAll(/<TargetFrameworks?(?:\s[^>]*)?>([^<]*)<\/TargetFrameworks?>/g)];
  if (matches.length === 0) throw new Error(`${TEST_PROJECT} has no <TargetFramework(s)>`);
  const majors: number[] = [];
  for (const m of matches) {
    for (const t of m[1]!.split(";").map((t) => t.trim()).filter(Boolean)) {
      const n = /^net([0-9]+)\.0$/.exec(t);
      if (!n) throw new Error(`${TEST_PROJECT}: unexpected target "${t}"`);
      const major = Number(n[1]);
      if (!majors.includes(major)) majors.push(major);
    }
  }
  return majors;
}

/** One CI job that runs `dotnet test`, with the `<N>.0.x` majors its `setup-dotnet` installs. */
export interface TestJob {
  where: string; // "<workflow file> job <name>"
  sdks: number[];
}

/** The jobs of a workflow that run `dotnet test`, each with the SDK majors it installs. */
export function testJobs(workflowYaml: string, file: string): TestJob[] {
  type Step = { uses?: string; run?: string; with?: Record<string, unknown> };
  const wf = Bun.YAML.parse(workflowYaml) as { jobs?: Record<string, { steps?: Step[] }> };
  const out: TestJob[] = [];
  for (const [name, job] of Object.entries(wf.jobs ?? {})) {
    const steps = job.steps ?? [];
    if (!steps.some((s) => /\bdotnet\s+test\b/.test(s.run ?? ""))) continue;
    const sdks: number[] = [];
    for (const step of steps) {
      if (!step.uses?.startsWith("actions/setup-dotnet@")) continue;
      for (const v of String(step.with?.["dotnet-version"] ?? "").split(/\s+/).filter(Boolean)) {
        const n = /^(\d+)\.0\.x$/.exec(v);
        if (!n) throw new Error(`${file} job ${name}: unexpected dotnet-version "${v}"`);
        sdks.push(Number(n[1]));
      }
    }
    out.push({ where: `${file} job ${name}`, sdks });
  }
  return out;
}

/** The problems (empty = pass) and the notes for one set of inputs. */
export function verdict(
  supported: { major: number; eol?: string }[],
  targets: number[],
  jobs: TestJob[],
  today = "1970-01-01",
  upcoming: number[] | null = null,
): { problems: string[]; notes: string[] } {
  const problems: string[] = [];
  const notes: string[] = [];
  if (supported.length === 0) problems.push("the release index lists no supported .NET line (index format changed?)");
  if (jobs.length === 0) problems.push(`no CI job runs \`dotnet test\` (in ${WORKFLOWS})`);
  const required = supported.filter((s) => !isLeaving(s.eol ?? "none", today));
  for (const s of supported) {
    if (!required.includes(s)) notes.push(`.NET ${s.major}.0 is leaving (end of support ${s.eol}): not required`);
  }
  if (supported.length > 0 && required.length === 0) problems.push("every supported .NET line is leaving: none would be tested");
  for (const { major } of required) {
    if (!targets.includes(major)) problems.push(`.NET ${major}.0 is supported but not a test target: add net${major}.0 to ${TEST_PROJECT}`);
    for (const job of jobs) {
      if (!job.sdks.includes(major)) problems.push(`.NET ${major}.0 is supported but ${job.where} does not install it: add ${major}.0.x there`);
    }
  }
  for (const t of targets) {
    if (!supported.some((s) => s.major === t)) {
      notes.push(upcoming !== null && upcoming.includes(t)
        ? `net${t}.0 is a test target ahead of its GA (${t}.0 is not released yet): not required`
        : `net${t}.0 is still a test target but no longer supported (kept while the package ships that asset?)`);
    }
  }
  return { problems, notes };
}

async function check(): Promise<number> {
  // One time limit for the whole fetch, request to last byte (a stalled fetch once held a local
  // run for over 15 minutes, 2026-11-09; in CI it would hold the runner): fail loudly instead.
  const limit = new AbortController();
  const timer = setTimeout(() => limit.abort(), FETCH_SECONDS * 1000);
  let body: string;
  try {
    const res = await fetch(INDEX, { signal: limit.signal });
    if (!res.ok) throw new Error(`HTTP ${res.status} for ${INDEX}`);
    body = await res.text();
  } catch (e) {
    if (limit.signal.aborted) throw new Error(`no complete response from ${INDEX} within ${FETCH_SECONDS} s`);
    throw e;
  } finally {
    clearTimeout(timer);
  }
  const index = JSON.parse(body) as { "releases-index": Channel[] };
  const today = new Date().toISOString().slice(0, 10);
  const supported = supportedLines(index["releases-index"], today);
  const targets = testTargets(readFileSync(join(ROOT, TEST_PROJECT), "utf8"));
  const jobs = readdirSync(join(ROOT, WORKFLOWS))
    .filter((f) => /\.ya?ml$/.test(f))
    .sort()
    .flatMap((f) => testJobs(readFileSync(join(ROOT, WORKFLOWS, f), "utf8"), `${WORKFLOWS}/${f}`));
  console.log(`supported on ${today}: ${supported.map((s) => `${s.major}.0 ${s.type.toUpperCase()} (EOL ${s.eol})`).join(", ")}`);
  console.log(`test targets: ${targets.map((t) => `net${t}.0`).join(", ")}`);
  for (const j of jobs) console.log(`${j.where} (runs dotnet test): SDKs ${j.sdks.map((s) => `${s}.0.x`).join(", ")}`);
  const { problems, notes } = verdict(supported, targets, jobs, today, upcomingLines(index["releases-index"]));
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

  const yaml =
    "jobs:\n  v:\n    steps:\n      - uses: actions/checkout@abc\n      - uses: actions/setup-dotnet@def # v4\n        with:\n          dotnet-version: |\n            8.0.x\n            10.0.x\n      - run: dotnet test tests\n" +
    "  push:\n    steps:\n      - uses: actions/setup-dotnet@def\n        with:\n          dotnet-version: 10.0.x\n      - run: dotnet nuget push x\n";
  const jobs = testJobs(yaml, "wf.yml");
  ok("only the job that runs dotnet test", jobs.length === 1 && jobs[0]!.where === "wf.yml job v");
  ok("its sdks", jobs[0]!.sdks.join() === "8,10");
  ok("ci odd sdk throws", throws(() => testJobs(yaml.replace("8.0.x", "8.x"), "wf.yml")));

  const sup = [{ major: 8 }, { major: 9 }, { major: 10 }];
  const job = (where: string, sdks: number[]): TestJob => ({ where, sdks });
  ok("all covered passes", verdict(sup, [8, 9, 10], [job("a", [8, 9, 10]), job("b", [8, 9, 10])]).problems.length === 0);
  const missing = verdict(sup, [8, 10], [job("a", [8, 10])]).problems;
  ok("a missing line fails twice, naming the csproj and the job", missing.length === 2 && missing[0]!.includes(TEST_PROJECT) && missing[1]!.includes("a does not"));
  const oneJob = verdict(sup, [8, 9, 10], [job("a", [8, 9, 10]), job("b", [8, 10])]).problems;
  ok("a second test job missing a line fails, naming it", oneJob.length === 1 && oneJob[0]!.includes("b does not"));
  const extra = verdict([{ major: 10 }], [8, 10], [job("a", [8, 10])]);
  ok("an unsupported target is a note, not a failure", extra.problems.length === 0 && extra.notes.length === 1);
  ok("an empty index fails", verdict([], [10], [job("a", [10])]).problems.length === 1);
  ok("no test job fails", verdict([{ major: 10 }], [10], []).problems.length === 1);

  const lines = [{ major: 8, eol: "2026-11-10" }, { major: 9, eol: "2026-11-10" }, { major: 10, eol: "2028-11-14" }];
  const only10 = verdict(lines, [10], [job("a", [10])], "2026-11-07");
  ok("lines within 30 days of EOL are leaving, not required", only10.problems.length === 0 && only10.notes.filter((n) => n.includes("leaving")).length === 2);
  ok("31 days before EOL a line is still required", verdict(lines, [10], [job("a", [10])], "2026-10-10").problems.length === 4);
  ok("the 30-day boundary is leaving", isLeaving("2026-12-07", "2026-11-07") && !isLeaving("2026-12-08", "2026-11-07"));
  ok("no EOL date is never leaving", !isLeaving("none", "2099-01-01"));
  ok("all leaving fails", verdict([{ major: 9, eol: "2026-11-10" }], [9], [job("a", [9])], "2026-11-07").problems.length === 1);

  // Ahead of a GA: a conditional target element counts, and the target is a note, not a failure.
  ok("conditional targets read too", testTargets(
    '<TargetFrameworks>net10.0</TargetFrameworks>\n<TargetFrameworks Condition="$(X) &gt;= 11">net10.0;net11.0</TargetFrameworks>').join() === "10,11");
  ok("upcoming lines are go-live / preview", upcomingLines(index).join() === "11");
  const ahead = verdict([{ major: 10, eol: "2028-11-14" }], [10, 11], [job("a", [10, 11])], "2026-11-08", [11]);
  ok("a target ahead of its GA is a note, not a failure", ahead.problems.length === 0 && ahead.notes.filter((n) => n.includes("ahead of its GA")).length === 1);

  console.log(`selftest: ${passed}/${passed + failed} passed`);
  return failed === 0 ? 0 : 1;
}

const cmd = process.argv[2];
try {
  process.exit(cmd === "selftest" ? selftest() : cmd === undefined ? await check() : (console.error("usage: runtimes.ts [selftest]"), 2));} catch (e) {
  console.error(`runtimes: ${(e as Error).message}`);
  process.exit(1);
}
